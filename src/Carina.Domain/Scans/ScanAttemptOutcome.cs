namespace Carina.Domain.Scans;

public enum ScanAttemptOutcome
{
    Succeeded = 1,

    NoLock = 2,

    LockedWithoutData = 3,

    IncompleteTables = 4,

    UnexpectedStream = 5,
}
