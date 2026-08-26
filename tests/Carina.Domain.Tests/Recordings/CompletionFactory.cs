using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

internal static class CompletionFactory
{
    public static readonly DateTime WindowStart = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    public static readonly DateTime WindowEnd = WindowStart.AddSeconds(1000);

    public static readonly TimeSpan WholeWindow = TimeSpan.FromSeconds(1000);

    public static readonly ExpectedBitrate Bitrate = new(16_000_000, 24_000_000);

    public static readonly CompletionTolerance Tolerance = new(0.995, 0.95, 10);

    public const long TypicalBytes = 2_500_000_000;

    public static readonly IReadOnlyList<RecordingFault> FaultsTheCrossCheckNames =
    [
        RecordingFault.NothingLanded,
        RecordingFault.SizeUnobserved,
        RecordingFault.StoppedUnasked,
        RecordingFault.ShortOfTheWindow,
        RecordingFault.LighterThanTheStream,
        RecordingFault.HeavierThanTheStream,
    ];

    public static RecordingEvidence Evidence(
        long? bytes = TypicalBytes,
        TimeSpan? written = null,
        bool asked = true)
        => new(bytes, written ?? WholeWindow, WindowStart, WindowEnd, asked ? WindowEnd : null);

    public static RecordingVerdict Judge(
        long? bytes = TypicalBytes,
        TimeSpan? written = null,
        bool asked = true)
        => CompletionEvaluator.Judge(Evidence(bytes, written, asked), Bitrate, Tolerance);

    public static RecordingVerdict Judge(RecordingEvidence evidence)
        => CompletionEvaluator.Judge(evidence, Bitrate, Tolerance);

    public static RecordingVerdict JudgeBy(RecordingEvidence evidence, CompletionTolerance tolerance)
        => CompletionEvaluator.Judge(evidence, Bitrate, tolerance);
}
