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

public sealed record OutcomeDetail(RecordingFault Fault, TuneFailureKind? TuneFailure, string Note, DateTime NoticedAt);

public sealed record Interruption(RecordingFault Fault, DateTime OccurredAt, DateTime? ResumedAt);
