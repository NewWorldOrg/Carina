using Carina.Domain.Segments;

namespace Carina.TestSupport;

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
