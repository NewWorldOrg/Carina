namespace Carina.Domain.Segments;

/// <summary>
/// How many entries the catalog holds of each kind.
/// </summary>
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

/// <summary>
/// Reads how many entries the catalog holds of each kind.
/// </summary>
public interface ICatalogCountsReader
{
    Task<CatalogCounts> ReadAsync(CancellationToken cancellationToken);
}
