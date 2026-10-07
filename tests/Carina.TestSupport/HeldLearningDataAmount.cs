using Carina.Domain.Segments;

namespace Carina.TestSupport;

/// <summary>
/// How much learning data is kept, as a test sets it. Nothing kept until it is set.
/// </summary>
public sealed class HeldLearningDataAmount : ILearningDataAmountReader
{
    public LearningDataAmount Amount { get; set; } = LearningDataAmount.None;

    public int Reads { get; private set; }

    public Task<LearningDataAmount> ReadAsync(CancellationToken cancellationToken)
    {
        Reads++;

        return Task.FromResult(Amount);
    }
}
