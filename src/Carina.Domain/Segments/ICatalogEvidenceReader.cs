namespace Carina.Domain.Segments;

/// <summary>
/// Reads what an evaluation would stand on as the catalog stands now.
/// </summary>
public interface ICatalogEvidenceReader
{
    /// <summary>
    /// The recordings whose learning data was read, to its end or as far as it could be, that the catalog is sure of
    /// an advert in, and the outer edges of the pods of those adverts, as <see cref="EvaluationEvidence.Of(IReadOnlyList{CatalogTruth}, DateTime?)"/>
    /// counts them.
    /// </summary>
    Task<EvaluationEvidence> ReadAsync(CancellationToken cancellationToken);
}
