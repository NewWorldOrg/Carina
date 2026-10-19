namespace Carina.Domain.Segments;

/// <summary>
/// What a round of learning reads to choose the recordings it learns from.
/// </summary>
public interface ITrainingWorklist
{
    /// <summary>
    /// Every recording whose learning data was read to its end or as far as it could be, whether or not the
    /// recording is still kept, that has a stretch the catalog heard in it or a skip event, with what it is learned
    /// by.
    /// </summary>
    Task<IReadOnlyList<LearningCandidate>> CandidatesAsync(CancellationToken cancellationToken);
}
