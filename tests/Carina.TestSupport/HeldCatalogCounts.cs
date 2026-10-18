using Carina.Domain.Segments;

namespace Carina.TestSupport;

/// <summary>
/// How many entries the catalog holds of each kind, as a test sets it. None until it is set.
/// </summary>
public sealed class HeldCatalogCounts : ICatalogCountsReader
{
    public CatalogCounts Counts { get; set; } = CatalogCounts.None;

    public int Reads { get; private set; }

    public Task<CatalogCounts> ReadAsync(CancellationToken cancellationToken)
    {
        Reads++;

        return Task.FromResult(Counts);
    }
}
