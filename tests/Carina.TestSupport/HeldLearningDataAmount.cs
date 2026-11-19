using Carina.Domain.Segments;

namespace Carina.TestSupport;

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
