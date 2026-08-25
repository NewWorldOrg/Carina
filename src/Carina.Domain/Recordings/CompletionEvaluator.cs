namespace Carina.Domain.Recordings;

public static class CompletionEvaluator
{
    public static RecordingVerdict Judge(
        RecordingEvidence evidence,
        ExpectedBitrate bitrate,
        CompletionTolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(bitrate);
        ArgumentNullException.ThrowIfNull(tolerance);

        List<RecordingFinding> findings = [];
        TimeSpan? window = WindowOf(evidence);

        if (window is null)
        {
            findings.Add(RecordingFinding.WindowUnknown);
        }

        if (evidence.FileSizeBytes is null)
        {
            findings.Add(RecordingFinding.SizeUnknown);
        }
        else if (evidence.FileSizeBytes is 0)
        {
            findings.Add(RecordingFinding.NothingLanded);
        }

        if (evidence.Written is null)
        {
            findings.Add(RecordingFinding.LengthUnknown);
        }

        if (evidence.AbortedAt is null)
        {
            findings.Add(RecordingFinding.NobodyAskedItToStop);
        }

        double? coverage = window is { } span && evidence.Written is { } written
            ? (double)written.Ticks / span.Ticks
            : null;

        if (coverage is { } reached && reached < tolerance.CompleteCoverage)
        {
            findings.Add(RecordingFinding.ShortOfTheWindow);
        }

        WeighTheFile(evidence, bitrate, tolerance, findings);

        return RecordingVerdict.Of(Decide(findings, coverage, tolerance), coverage, findings);
    }

    private static TimeSpan? WindowOf(RecordingEvidence evidence)
        => evidence.WindowStart is { } start && evidence.WindowEnd is { } end && end > start
            ? end - start
            : null;

    private static void WeighTheFile(
        RecordingEvidence evidence,
        ExpectedBitrate bitrate,
        CompletionTolerance tolerance,
        List<RecordingFinding> findings)
    {
        if (evidence.FileSizeBytes is not { } bytes || evidence.Written is not { } written)
        {
            return;
        }

        if (bytes > 0 && (bytes * 100) < (bitrate.LeastBytesOver(written) * (100 - tolerance.SizeSlackPercent)))
        {
            findings.Add(RecordingFinding.LighterThanTheStream);
        }

        if ((bytes * 100) > (bitrate.MostBytesOver(written) * (100 + tolerance.SizeSlackPercent)))
        {
            findings.Add(RecordingFinding.HeavierThanTheStream);
        }
    }

    private static RecordingOutcome Decide(
        IReadOnlyList<RecordingFinding> findings,
        double? coverage,
        CompletionTolerance tolerance)
    {
        if (findings.Contains(RecordingFinding.NothingLanded))
        {
            return RecordingOutcome.Failed;
        }

        if (findings.Contains(RecordingFinding.SizeUnknown))
        {
            return RecordingOutcome.Failed;
        }

        if (coverage is not { } reached)
        {
            return RecordingOutcome.Failed;
        }

        if (reached < tolerance.TruncatedCoverage)
        {
            return RecordingOutcome.Failed;
        }

        if (findings.Contains(RecordingFinding.NobodyAskedItToStop))
        {
            return RecordingOutcome.Truncated;
        }

        if (reached < tolerance.CompleteCoverage)
        {
            return RecordingOutcome.Truncated;
        }

        if (findings.Contains(RecordingFinding.LighterThanTheStream))
        {
            return RecordingOutcome.Truncated;
        }

        return RecordingOutcome.Complete;
    }
}
