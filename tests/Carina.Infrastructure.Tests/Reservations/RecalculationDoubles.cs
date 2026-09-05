using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Reservations;

internal sealed class GatedSeating(TunerCapacity capacity, bool throws = false) : ITunerCapacityDirectory
{
    private readonly Lock gate = new();

    private int inside;

    public int Most { get; private set; }

    public int Entered { get; private set; }

    public TaskCompletionSource? Hold { get; set; }

    public TaskCompletionSource Arrived { get; } = new();

    public async Task<TunerCapacity?> ReadAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            inside++;
            Entered++;
            Most = Math.Max(Most, inside);
        }

        Arrived.TrySetResult();

        try
        {
            if (Hold is { } held)
            {
                await held.Task;
            }

            return throws
                ? throw new InvalidOperationException("the seating would not answer")
                : capacity;
        }
        finally
        {
            lock (gate)
            {
                inside--;
            }
        }
    }
}

internal sealed class WatchedProgrammes : IProgrammeRepository
{
    private readonly HeldProgrammes held = new();

    public List<Programme> Held => held.Programmes;

    public List<long> AskedFrom { get; } = [];

    public Exception? Throws { get; set; }

    public Task<Programme?> FindAsync(ProgrammeId id, CancellationToken cancellationToken)
        => held.FindAsync(id, cancellationToken);

    public Task<IReadOnlyList<Programme>> ListAsync(ProgrammeWindow window, CancellationToken cancellationToken)
        => held.ListAsync(window, cancellationToken);

    public Task<IReadOnlyList<Programme>> ListForServicesAsync(
        IReadOnlyList<ProgrammeService> services,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
        => held.ListForServicesAsync(services, from, to, cancellationToken);

    public Task AddAsync(Programme programme, CancellationToken cancellationToken)
        => held.AddAsync(programme, cancellationToken);

    public Task<ProgrammesAbsorbed> AbsorbAsync(
        IReadOnlyList<ProgrammeBroadcast> broadcasts,
        DateTime at,
        CancellationToken cancellationToken)
        => held.AbsorbAsync(broadcasts, at, cancellationToken);

    public Task<IReadOnlyList<Programme>> ListEndedBeforeAsync(
        DateTime at,
        int rows,
        CancellationToken cancellationToken)
        => held.ListEndedBeforeAsync(at, rows, cancellationToken);

    public Task<int> ForgetAsync(IReadOnlyList<Programme> programmes, CancellationToken cancellationToken)
        => held.ForgetAsync(programmes, cancellationToken);

    public Task<DateTime?> CoveredUntilAsync(int networkId, int serviceId, CancellationToken cancellationToken)
        => held.CoveredUntilAsync(networkId, serviceId, cancellationToken);

    public Task<IReadOnlyList<Programme>> ListAfterAsync(
        long revision,
        int rows,
        CancellationToken cancellationToken)
    {
        AskedFrom.Add(revision);

        return Throws is { } refusal
            ? throw refusal
            : held.ListAfterAsync(revision, rows, cancellationToken);
    }

    public Task<int> ForgetEverythingAsync(CancellationToken cancellationToken)
        => held.ForgetEverythingAsync(cancellationToken);
}

internal sealed class RushedClock(DateTime now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        => base.CreateTimer(callback, state, TimeSpan.FromMilliseconds(1), period);
}
