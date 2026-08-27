using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;

namespace Carina.Infrastructure.Tests.Reservations;

internal sealed class FixedClock(DateTime now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
}

internal sealed class HeldSeating(TunerCapacity? capacity, WatchedWrite? write = null) : ITunerCapacityDirectory
{
    public int Reads { get; private set; }

    public int ReadWhileWriting { get; private set; }

    public Task<TunerCapacity?> ReadAsync(CancellationToken cancellationToken)
    {
        Reads++;

        if (write is { Open: true })
        {
            ReadWhileWriting++;
        }

        return Task.FromResult(capacity);
    }
}

internal sealed class TuningByService(WatchedWrite? write = null) : IServiceTuningDirectory
{
    private readonly Dictionary<int, TuningResolution> answers = [];

    public List<int> Asked { get; } = [];

    public int AskedWhileWriting { get; private set; }

    public TuningResolution Otherwise { get; set; } =
        TuningResolution.Refused(TuningRefusal.NoSelectedChannel);

    public void Answer(int serviceId, TuningResolution resolution) => answers[serviceId] = resolution;

    public void Answer(int serviceId, TuningParameters tuning)
        => Answer(serviceId, TuningResolution.Tunable(new CandidateChannelId(Guid.NewGuid()), tuning, impaired: false));

    public Task<TuningResolution> ResolveTuningAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken)
    {
        Asked.Add(serviceId.Value);

        if (write is { Open: true })
        {
            AskedWhileWriting++;
        }

        return Task.FromResult(answers.TryGetValue(serviceId.Value, out TuningResolution? held) ? held : Otherwise);
    }

    public Task<bool> CanTuneAsync(NetworkId networkId, ServiceId serviceId, CancellationToken cancellationToken)
        => Task.FromResult(
            (answers.TryGetValue(serviceId.Value, out TuningResolution? held) ? held : Otherwise).CanTune);
}

internal sealed class WatchedWrite : IAtomicWrite
{
    public int Opened { get; private set; }

    public int Committed { get; private set; }

    public int RolledBack { get; private set; }

    public bool Open { get; private set; }

    public async Task<T> AllOrNothingAsync<T>(
        Func<CancellationToken, Task<T>> write,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);

        Opened++;
        Open = true;

        try
        {
            T written = await write(cancellationToken);
            Committed++;

            return written;
        }
        catch
        {
            RolledBack++;

            throw;
        }
        finally
        {
            Open = false;
        }
    }
}

internal sealed class HeldReservations(IAtomicWrite? write = null) : IReservationRepository
{
    private readonly List<Reservation> held = [];

    public IReadOnlyList<Reservation> Held => held;

    public List<string> Wrote { get; } = [];

    public List<string> WroteOutsideAWrite { get; } = [];

    public Exception? RefuseToAdd { get; set; }

    public List<Reservation> ArrivesAfterTheFirstList { get; } = [];

    public int Lists { get; private set; }

    public Task<Reservation?> FindAsync(ReservationId id, CancellationToken cancellationToken)
        => Task.FromResult(held.FirstOrDefault(reservation => reservation.Id.Equals(id)));

    public Task<Reservation?> FindByProgrammeAsync(ProgrammeRef programme, CancellationToken cancellationToken)
        => Task.FromResult(held.FirstOrDefault(reservation => reservation.Programme.Equals(programme)));

    public Task<IReadOnlyList<Reservation>> ListPendingAsync(
        ReservationWindow window,
        CancellationToken cancellationToken)
    {
        Lists++;

        IReadOnlyList<Reservation> pending =
        [
            .. held
                .Where(reservation => reservation.RecordingOutcome is null)
                .Where(reservation => reservation.State
                    is ReservationState.Scheduled or ReservationState.Conflict)
                .Where(reservation => reservation.IsPinned
                                      || (reservation.EndAt >= window.From && reservation.StartAt <= window.To))
                .OrderBy(reservation => reservation.StartAt)
                .ThenBy(reservation => reservation.Id.Value),
        ];

        if (Lists is 1 && ArrivesAfterTheFirstList.Count > 0)
        {
            held.AddRange(ArrivesAfterTheFirstList);
            ArrivesAfterTheFirstList.Clear();
        }

        return Task.FromResult(pending);
    }

    public Task<IReadOnlyList<Reservation>> ListForRuleAsync(RuleId ruleId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Reservation>>(
            [.. held.Where(reservation => ruleId.Equals(reservation.RuleId))]);

    public Task<IReadOnlyList<Reservation>> ListForBroadcastGroupAsync(
        BroadcastGroupKey key,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Reservation>>(
            [.. held.Where(reservation => key.Equals(reservation.BroadcastGroupKey))]);

    public Task AddAsync(Reservation reservation, CancellationToken cancellationToken)
    {
        if (RefuseToAdd is { } refusal)
        {
            throw refusal;
        }

        Note($"add {reservation.Id.Value}");
        held.Add(reservation);

        return Task.CompletedTask;
    }

    public Task SaveAsync(Reservation reservation, CancellationToken cancellationToken)
    {
        Note($"save {reservation.Id.Value}");

        return Task.CompletedTask;
    }

    public Task SaveAllAsync(IReadOnlyList<Reservation> reservations, CancellationToken cancellationToken)
    {
        foreach (Reservation reservation in reservations)
        {
            Note($"save {reservation.Id.Value}");
        }

        return Task.CompletedTask;
    }

    public Task WithdrawAsync(IReadOnlyList<Reservation> reservations, CancellationToken cancellationToken)
    {
        foreach (Reservation reservation in reservations)
        {
            Note($"withdraw {reservation.Id.Value}");
            held.Remove(reservation);
        }

        return Task.CompletedTask;
    }

    public void Standing(params Reservation[] reservations) => held.AddRange(reservations);

    private void Note(string what)
    {
        Wrote.Add(what);

        if (write is WatchedWrite { Open: false })
        {
            WroteOutsideAWrite.Add(what);
        }
    }
}
