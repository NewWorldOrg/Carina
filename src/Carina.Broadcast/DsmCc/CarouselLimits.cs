namespace Carina.Broadcast.DsmCc;

public sealed class CarouselLimits
{
    public const long LargestAModuleCanBe = (long)ModuleAssembler.LargestBlock * ModuleAssembler.MostBlocks;

    private const long Mebibyte = 1024 * 1024;

    public CarouselLimits(int mostCarousels, int mostModules, long largestModule, long largestTotal, int mostParts)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mostCarousels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mostModules);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(largestModule);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mostParts);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(largestModule, Math.Min(LargestAModuleCanBe, int.MaxValue));
        ArgumentOutOfRangeException.ThrowIfLessThan(largestTotal, largestModule);

        MostCarousels = mostCarousels;
        MostModules = mostModules;
        LargestModule = largestModule;
        LargestTotal = largestTotal;
        MostParts = mostParts;
    }

    public static CarouselLimits Broadcast { get; } = new(16, 512, 16 * Mebibyte, 64 * Mebibyte, 1024);

    public int MostCarousels { get; }

    public int MostModules { get; }

    public long LargestModule { get; }

    public long LargestTotal { get; }

    public int MostParts { get; }
}
