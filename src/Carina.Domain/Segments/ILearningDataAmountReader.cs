namespace Carina.Domain.Segments;

public interface ILearningDataAmountReader
{
    Task<LearningDataAmount> ReadAsync(CancellationToken cancellationToken);
}
