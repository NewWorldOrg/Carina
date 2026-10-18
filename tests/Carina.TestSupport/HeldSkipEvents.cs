using Carina.Domain.Segments;

namespace Carina.TestSupport;

/// <summary>
/// The skip events held in memory, taken as the store takes them: no more than
/// <see cref="SkipEvent.MostAnHour"/> for one recording within <see cref="SkipEvent.Within"/> before each.
/// </summary>
public sealed class HeldSkipEvents : ISkipEventRepository
{
    private readonly Lock gate = new();

    private readonly List<SkipEvent> taken = [];

    public IReadOnlyList<SkipEvent> Taken
    {
        get
        {
            lock (gate)
            {
                return [.. taken];
            }
        }
    }

    public Task<bool> TakeAsync(SkipEvent skip, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(skip);

        lock (gate)
        {
            DateTime since = skip.HappenedAt - SkipEvent.Within;

            if (taken.Count(kept => kept.RecordingId.Equals(skip.RecordingId) && kept.HappenedAt > since) >= SkipEvent.MostAnHour)
            {
                return Task.FromResult(false);
            }

            taken.Add(SkipEvent.Rehydrate(taken.Count + 1, skip.RecordingId, skip.HappenedAt, skip.Event, skip.Kind, skip.From, skip.To, skip.ModelVersion));

            return Task.FromResult(true);
        }
    }

    public Task<SkipReturns> ReturnsAsync(int modelVersion, DateTime from, DateTime until, CancellationToken cancellationToken)
        => Task.FromResult(SkipReturns.Of(
            Taken.Select(skip => new ViewerAction(skip.HappenedAt, skip.Event, skip.Kind, skip.From, skip.To, skip.ModelVersion)),
            modelVersion,
            from,
            until));
}
