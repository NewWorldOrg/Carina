using Carina.Domain.Segments;

namespace Carina.TestSupport;

/// <summary>
/// The rounds of learning taken, held in memory one per day, as the store keeps them.
/// </summary>
public sealed class HeldSegmentLearningRounds : ISegmentLearningRoundRepository
{
    private readonly Lock gate = new();

    private readonly Dictionary<DateOnly, SegmentLearningRound> taken = [];

    public IReadOnlyList<SegmentLearningRound> Taken
    {
        get
        {
            lock (gate)
            {
                return [.. taken.Values.OrderBy(round => round.Day)];
            }
        }
    }

    public Task<bool> TakenAsync(DateOnly day, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(taken.ContainsKey(day));
        }
    }

    public Task<bool> TakeAsync(SegmentLearningRound round, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(round);

        lock (gate)
        {
            return Task.FromResult(taken.TryAdd(round.Day, round));
        }
    }
}
