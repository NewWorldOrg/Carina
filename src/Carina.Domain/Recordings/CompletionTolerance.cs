namespace Carina.Domain.Recordings;

public sealed record CompletionTolerance
{
    public static readonly CompletionTolerance Default = new(0.995, 0.95, 10);

    public CompletionTolerance(double completeCoverage, double truncatedCoverage, int sizeSlackPercent)
    {
        WithinTheWindow(completeCoverage, nameof(completeCoverage));
        WithinTheWindow(truncatedCoverage, nameof(truncatedCoverage));

        if (truncatedCoverage > completeCoverage)
        {
            throw new ArgumentOutOfRangeException(
                nameof(truncatedCoverage),
                truncatedCoverage,
                $"A recording is warned about before it is failed, so the warning band starts at or below {completeCoverage}.");
        }

        if (sizeSlackPercent is < 0 or >= 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sizeSlackPercent),
                sizeSlackPercent,
                "A slack of a hundred percent puts the bottom of the allowed weight at nothing, "
                + "so no file is ever light enough to be doubted.");
        }

        CompleteCoverage = completeCoverage;
        TruncatedCoverage = truncatedCoverage;
        SizeSlackPercent = sizeSlackPercent;
    }

    public double CompleteCoverage { get; }

    public double TruncatedCoverage { get; }

    public int SizeSlackPercent { get; }

    private static void WithinTheWindow(double coverage, string parameterName)
    {
        if (!double.IsFinite(coverage) || coverage is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                coverage,
                "A coverage is the part of the window that was written, so it lies between nothing and all of it.");
        }
    }
}
