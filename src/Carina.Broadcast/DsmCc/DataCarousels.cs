using Carina.Broadcast.Sections;
using Carina.Broadcast.Tables;

namespace Carina.Broadcast.DsmCc;

public sealed class DataCarousels
{
    private static readonly IReadOnlyList<CarouselChange> Nothing = [];

    private readonly CarouselLimits limits;
    private readonly Dictionary<int, ModuleAssembler> carousels = [];

    public DataCarousels()
        : this(CarouselLimits.Broadcast)
    {
    }

    public DataCarousels(CarouselLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        this.limits = limits;
    }

    public IReadOnlyList<CarouselChange> Push(int componentTag, Section section)
    {
        ArgumentNullException.ThrowIfNull(section);

        return section.TableId switch
        {
            DownloadInfoIndication.TableId => Indicate(componentTag, DownloadInfoIndication.Read(section)),
            DownloadDataBlock.TableId => Deliver(componentTag, DownloadDataBlock.Read(section)),
            _ => Nothing,
        };
    }

    private IReadOnlyList<CarouselChange> Indicate(int componentTag, TableRead<DownloadInfoIndication> read)
    {
        if (read is not TableRead<DownloadInfoIndication>.Parsed parsed)
        {
            return Unreadable(componentTag, read);
        }

        if (!carousels.TryGetValue(componentTag, out ModuleAssembler? assembler))
        {
            if (carousels.Count >= limits.MostCarousels)
            {
                return [new CarouselChange.Dropped(componentTag, CarouselDefect.TooManyCarousels)];
            }

            assembler = new ModuleAssembler(componentTag, limits);
            carousels[componentTag] = assembler;
        }

        long others = carousels.Values.Where(other => other != assembler).Sum(other => other.DeclaredSize);
        IReadOnlyList<CarouselChange> changes = assembler.Accept(parsed.Table, limits.LargestTotal - others);

        if (changes.Any(change => change is CarouselChange.Dropped))
        {
            carousels.Remove(componentTag);
        }

        return changes;
    }

    private IReadOnlyList<CarouselChange> Deliver(int componentTag, TableRead<DownloadDataBlock> read)
    {
        if (read is not TableRead<DownloadDataBlock>.Parsed parsed)
        {
            return Unreadable(componentTag, read);
        }

        return carousels.TryGetValue(componentTag, out ModuleAssembler? assembler)
            ? assembler.Accept(parsed.Table)
            : [new CarouselChange.Rejected(componentTag, CarouselDefect.NotInCatalogue, parsed.Table.ModuleId)];
    }

    private static IReadOnlyList<CarouselChange> Unreadable<TTable>(int componentTag, TableRead<TTable> read)
        where TTable : class
        => read is TableRead<TTable>.Rejected rejected ? [new CarouselChange.Unreadable(componentTag, rejected.Defect)] : Nothing;
}
