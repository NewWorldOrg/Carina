namespace Carina.Domain.Recordings;

public enum ThumbnailFault
{
    ProgrammeMissing = 1,

    SourceOutOfReach = 2,

    Refused = 3,

    TimedOut = 4,

    NothingWasWritten = 5,
}
