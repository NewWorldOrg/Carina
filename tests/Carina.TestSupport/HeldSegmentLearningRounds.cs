using Carina.Domain.Segments;

namespace Carina.TestSupport;

/// <summary>
/// The rounds of learning finished, held in memory one per day, as the store keeps them, and the turn to take one,
/// which a test can say another process holds.
/// </summary>
public sealed class HeldSegmentLearningRounds : ISegmentLearningRoundRepository
{
    private readonly Lock gate = new();

    private readonly Dictionary<DateOnly, SegmentLearningRound> taken = [];

    private bool held;

    public bool HeldElsewhere { get; set; }

    public int Holds { get; private set; }

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

    public Task<DateTime?> LastLearnedAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(taken.Values.Where(round => round.Candidate is not null).Max(round => (DateTime?)round.TakenAt));
        }
    }

    public Task<SegmentLearningRound?> LatestAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(taken.Values.Where(round => round.Outcome is not null).MaxBy(round => round.Day));
        }
    }

    public Task<SegmentLearningRound?> LatestMeasuredAsync(int version, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(taken.Values.Where(round => round.InUseVersion == version && round.InUseMetrics is not null).MaxBy(round => round.Day));
        }
    }

    public Task<IAsyncDisposable?> HoldAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (HeldElsewhere || held)
            {
                return Task.FromResult<IAsyncDisposable?>(null);
            }

            held = true;
            Holds++;

            return Task.FromResult<IAsyncDisposable?>(new Turn(this));
        }
    }

    private sealed class Turn(HeldSegmentLearningRounds rounds) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            lock (rounds.gate)
            {
                rounds.held = false;
            }

            return ValueTask.CompletedTask;
        }
    }
}
