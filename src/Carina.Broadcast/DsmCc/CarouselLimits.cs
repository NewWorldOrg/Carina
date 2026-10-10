namespace Carina.Broadcast.DsmCc;

public sealed record CarouselLimits(int MostCarousels, int MostModules, long LargestModule, long LargestTotal, int MostParts)
{
    private const long Mebibyte = 1024 * 1024;

    public static CarouselLimits Broadcast { get; } = new(16, 512, 16 * Mebibyte, 64 * Mebibyte, 1024);
}
