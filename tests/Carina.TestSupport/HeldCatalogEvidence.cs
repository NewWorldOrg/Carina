using Carina.Domain.Segments;

namespace Carina.TestSupport;

/// <summary>
/// What the catalog would give an evaluation to stand on, as a test sets it. Nothing until it is set.
/// </summary>
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
