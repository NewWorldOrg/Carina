namespace Carina.Domain.Segments;

/// <summary>
/// Reads how much learning data is kept, counting the recordings it was taken from whether or not they are
/// still kept.
/// </summary>
public interface ILearningDataAmountReader
{
    Task<LearningDataAmount> ReadAsync(CancellationToken cancellationToken);
}
