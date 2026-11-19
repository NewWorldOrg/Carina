namespace Carina.Domain.Segments;

public interface ITrainingWorklist
{
    Task<IReadOnlyList<LearningCandidate>> CandidatesAsync(CancellationToken cancellationToken);
}
