namespace Carina.Domain.Segments;

public interface ICatalogEvidenceReader
{
    Task<EvaluationEvidence> ReadAsync(CancellationToken cancellationToken);
}
