namespace Carina.Domain.Segments;

public sealed record CatalogCounts(int Cm, int Opening, int Ending, int ProgrammeFixture, int StationFixture, int Unknown)
{
    public static CatalogCounts None { get; } = new(0, 0, 0, 0, 0, 0);

    public static CatalogCounts Of(IReadOnlyDictionary<CatalogKind, int> counted)
    {
        ArgumentNullException.ThrowIfNull(counted);

        return new CatalogCounts(
            counted.GetValueOrDefault(CatalogKind.Cm),
            counted.GetValueOrDefault(CatalogKind.Opening),
            counted.GetValueOrDefault(CatalogKind.Ending),
            counted.GetValueOrDefault(CatalogKind.ProgrammeFixture),
            counted.GetValueOrDefault(CatalogKind.StationFixture),
            counted.GetValueOrDefault(CatalogKind.Unknown));
    }
}

public interface ICatalogCountsReader
{
    Task<CatalogCounts> ReadAsync(CancellationToken cancellationToken);
}
