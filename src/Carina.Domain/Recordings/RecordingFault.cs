using Carina.Domain.Base;
using Carina.Domain.Channels;

namespace Carina.Domain.Recordings;

public enum RecordingFault
{
    TuneFailed = 1,

    RefusedByDiskPrecheck = 2,

    DiskExhausted = 3,

    DriverLost = 4,

    DrainGraceExpired = 5,

    StoppedByHand = 6,

    TunerContended = 7,

    ScramblingUnresolved = 8,

    ShortOfTheWindow = 9,
}

public sealed record OutcomeDetail(RecordingFault Fault, TuneFailureKind? TuneFailure, string Note, DateTime NoticedAt)
{
    public RecordingFault Fault { get; } = RecordingFaults.Named(Fault);

    public TuneFailureKind? TuneFailure { get; } = RecordingFaults.NamedTuneFailure(TuneFailure);

    public DateTime NoticedAt { get; } = UtcTimes.Required(NoticedAt, nameof(NoticedAt));
}

public sealed record Interruption(RecordingFault Fault, DateTime OccurredAt, DateTime? ResumedAt)
{
    public RecordingFault Fault { get; } = RecordingFaults.Named(Fault);

    public DateTime OccurredAt { get; } = UtcTimes.Required(OccurredAt, nameof(OccurredAt));

    public DateTime? ResumedAt { get; } = RecordingFaults.NotBefore(
        UtcTimes.Optional(ResumedAt, nameof(ResumedAt)),
        UtcTimes.Required(OccurredAt, nameof(OccurredAt)));

    public bool IsOpen => ResumedAt is null;
}

public static class RecordingFaults
{
    public static readonly IReadOnlyList<RecordingFault> ThatReachedTheTuner =
    [
        RecordingFault.TuneFailed,
        RecordingFault.DriverLost,
        RecordingFault.TunerContended,
        RecordingFault.ScramblingUnresolved,
    ];

    internal static RecordingFault Named(RecordingFault fault)
        => Enum.IsDefined(fault)
            ? fault
            : throw new ArgumentOutOfRangeException(nameof(fault), fault, "A fault is one the ledger holds.");

    internal static TuneFailureKind? NamedTuneFailure(TuneFailureKind? kind)
        => kind is null || Enum.IsDefined(kind.Value)
            ? kind
            : throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "A tune failure is one of the four kinds.");

    internal static DateTime? NotBefore(DateTime? resumedAt, DateTime occurredAt)
        => resumedAt is null || resumedAt >= occurredAt
            ? resumedAt
            : throw new ArgumentException(
                "A recording resumes after it was interrupted.",
                nameof(resumedAt));
}
