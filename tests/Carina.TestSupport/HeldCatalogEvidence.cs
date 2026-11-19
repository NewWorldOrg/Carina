using Carina.Domain.Segments;

namespace Carina.TestSupport;

public sealed class HeldCatalogEvidence : ICatalogEvidenceReader
{
    public EvaluationEvidence Evidence { get; set; } = new(0, 0, null);

    public int Reads { get; private set; }

    public Task<EvaluationEvidence> ReadAsync(CancellationToken cancellationToken)
    {
        Reads++;

        return Task.FromResult(Evidence);
    }
}
