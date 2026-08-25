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

        List<RecordingFault> faults = [];
        double coverage = (double)evidence.Written.Ticks / evidence.Window.Ticks;

        if (evidence.FileSizeBytes is null)
        {
            faults.Add(RecordingFault.SizeUnobserved);
        }
        else if (evidence.FileSizeBytes is 0)
        {
            faults.Add(RecordingFault.NothingLanded);
        }

        if (evidence.AbortedAt is null)
        {
            faults.Add(RecordingFault.StoppedUnasked);
        }

        if (coverage < tolerance.CompleteCoverage)
        {
            faults.Add(RecordingFault.ShortOfTheWindow);
        }

        WeighTheFile(evidence, bitrate, tolerance, faults);

        return RecordingVerdict.Of(Decide(faults, coverage, tolerance), coverage, faults);
    }

    private static void WeighTheFile(
        RecordingEvidence evidence,
        ExpectedBitrate bitrate,
        CompletionTolerance tolerance,
        List<RecordingFault> faults)
    {
        if (evidence.FileSizeBytes is not { } bytes)
        {
            return;
        }

        TimeSpan written = evidence.Written;

        if (bytes > 0 && (bytes * 100) < (bitrate.LeastBytesOver(written) * (100 - tolerance.SizeSlackPercent)))
        {
            faults.Add(RecordingFault.LighterThanTheStream);
        }

        if ((bytes * 100) > (bitrate.MostBytesOver(written) * (100 + tolerance.SizeSlackPercent)))
        {
            faults.Add(RecordingFault.HeavierThanTheStream);
        }
    }

    private static RecordingOutcome Decide(
        IReadOnlyList<RecordingFault> faults,
        double coverage,
        CompletionTolerance tolerance)
    {
        if (faults.Contains(RecordingFault.NothingLanded))
        {
            return RecordingOutcome.Failed;
        }

        if (faults.Contains(RecordingFault.SizeUnobserved))
        {
            return RecordingOutcome.Failed;
        }

        if (coverage < tolerance.TruncatedCoverage)
        {
            return RecordingOutcome.Failed;
        }

        if (faults.Contains(RecordingFault.StoppedUnasked))
        {
            return RecordingOutcome.Truncated;
        }

        if (faults.Contains(RecordingFault.ShortOfTheWindow))
        {
            return RecordingOutcome.Truncated;
        }

        if (faults.Contains(RecordingFault.LighterThanTheStream))
        {
            return RecordingOutcome.Truncated;
        }

        return RecordingOutcome.Complete;
    }
}
