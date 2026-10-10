namespace Carina.Domain.DataBroadcast;

/// <summary>
/// Why a whole carousel is left out: more carousels than are held, more modules in it than are held, or
/// more bytes in all than are held.
/// </summary>
public enum CarouselDropReason
{
    TooManyCarousels = 1,

    TooManyModules = 2,

    TotalTooLarge = 3,
}
