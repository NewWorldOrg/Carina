namespace Carina.Broadcast.DsmCc;

public enum CarouselDefect
{
    UnsupportedCompression = 1,

    ModuleTooLarge = 2,

    DecompressionFailed = 3,

    OriginalSizeExceeded = 4,

    InflatedSizeMismatch = 5,

    EntityMalformed = 6,

    NotInCatalogue = 7,

    VersionMismatch = 8,

    BlockOutOfRange = 9,

    BlockSizeMismatch = 10,

    BlockSizeOutOfRange = 11,

    BlockCountOutOfRange = 12,

    DuplicateModule = 13,

    TooManyModules = 14,

    TooManyCarousels = 15,

    TotalTooLarge = 16,
}
