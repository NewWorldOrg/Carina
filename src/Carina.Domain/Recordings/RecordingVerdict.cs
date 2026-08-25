namespace Carina.Domain.Recordings;

public enum RecordingFinding
{
    NothingLanded = 1,

    SizeUnknown = 2,

    LengthUnknown = 3,

    WindowUnknown = 4,

    NobodyAskedItToStop = 5,

    ShortOfTheWindow = 6,

    LighterThanTheStream = 7,

    HeavierThanTheStream = 8,
}

public sealed record RecordingVerdict
{
    private RecordingVerdict(RecordingOutcome outcome, double? coverage, IReadOnlyList<RecordingFinding> findings)
    {
        Outcome = outcome;
        Coverage = coverage;
        Findings = findings;
    }

    public RecordingOutcome Outcome { get; }

    public double? Coverage { get; }

    public IReadOnlyList<RecordingFinding> Findings { get; }

    public bool Names(RecordingFinding finding) => Findings.Contains(finding);

    internal static RecordingVerdict Of(
        RecordingOutcome outcome,
        double? coverage,
        IReadOnlyList<RecordingFinding> findings)
        => new(outcome, coverage, [.. findings]);
}
