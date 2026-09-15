namespace Carina.Api.Services;

public enum FindingDisposalFailure
{
    NoSuchFinding = 1,

    NamesARecording = 2,

    NothingOnTheDisk = 3,

    AlreadyThrownAway = 4,

    NoTimeWasTaken = 5,

    OneIsAlreadyBeingThrownAway = 6,

    RootOutOfReach = 7,

    FileChanged = 8,

    StillBeingWritten = 9,

    FilesLeftBehind = 10,

    DriverUnreachable = 11,

    DriverRefused = 12,

    TookTooLong = 13,
}
