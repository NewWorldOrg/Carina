namespace Carina.Broadcast.DsmCc;

public enum CarouselDefect
{
    UnsupportedCompression = 1,

    ModuleTooLarge = 2,

    DecompressionFailed = 3,

    OriginalSizeExceeded = 4,

    InflatedSizeMismatch = 5,

    EntityMalformed = 6,
}
