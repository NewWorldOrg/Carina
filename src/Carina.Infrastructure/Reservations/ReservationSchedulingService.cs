using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Reservations;

namespace Carina.Infrastructure.Reservations;

public sealed class ReservationSchedulingService(
    IReservationRepository reservations,
    ITunerCapacityDirectory seating,
    IServiceTuningDirectory tuning,
    IAtomicWrite write,
    RollingHorizon horizon,
    TimeProvider clock)
{
    public Task<SchedulingRun> CreateAsync(Reservation reservation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        return SettleAsync([reservation], null, null, cancellationToken);
    }

    public Task<SchedulingRun> ReviseAsync(
        Reservation reservation,
        ReservationRevision revision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentNullException.ThrowIfNull(revision);

        return SettleAsync([], reservation, revision, cancellationToken);
    }

    public Task<SchedulingRun> RecalculateAsync(CancellationToken cancellationToken)
        => SettleAsync([], null, null, cancellationToken);

    public async Task<SchedulingRun> PreviewAsync(
        IReadOnlyList<Reservation> proposed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposed);

        DateTime at = Moment();

        if (await seating.ReadAsync(cancellationToken) is not { } capacity)
        {
            return SchedulingRun.Refused(SchedulingRefusal.CapacityUnknown);
        }

        IReadOnlyList<Reservation> standing = await reservations.ListPendingAsync(Reaching(at), cancellationToken);
        Reservation[] considered = [.. standing, .. proposed];

        return await ResolveAsync(considered, cancellationToken) is { } selections
            ? Weigh(considered, selections, capacity, at)
            : SchedulingRun.Refused(SchedulingRefusal.CapacityUnknown);
    }

    private async Task<SchedulingRun> SettleAsync(
        IReadOnlyList<Reservation> joining,
        Reservation? revised,
        ReservationRevision? revision,
        CancellationToken cancellationToken)
    {
        DateTime at = Moment();

        if (await seating.ReadAsync(cancellationToken) is not { } capacity)
        {
            return SchedulingRun.Refused(SchedulingRefusal.CapacityUnknown);
        }

        IReadOnlyList<Reservation> looked = await reservations.ListPendingAsync(Reaching(at), cancellationToken);

        if (await ResolveAsync(Foreseen(looked, joining, revised), cancellationToken) is not { } selections)
        {
            return SchedulingRun.Refused(SchedulingRefusal.CapacityUnknown);
        }

        return await write.AllOrNothingAsync(
            async token =>
            {
                IReadOnlyList<Reservation> standing = await reservations.ListPendingAsync(Reaching(at), token);

                if (Foreseen(standing, joining, revised).Any(
                        reservation => !selections.ContainsKey(Naming(reservation))))
                {
                    return SchedulingRun.Refused(SchedulingRefusal.SomethingArrivedWhileReading);
                }

                Reservation[] considered = revised is null
                    ? [.. standing, .. joining]
                    : Alongside(standing, joining, revised, Applied(revised, revision!));

                SchedulingRun run = Weigh(considered, selections, capacity, at);

                if (!run.Settled)
                {
                    return run;
                }

                Apply(run.Plan, considered, at);

                await reservations.SaveAllAsync(Touched(standing, revised), token);

                foreach (Reservation joined in joining)
                {
                    await reservations.AddAsync(joined, token);
                }

                return run;
            },
            cancellationToken);
    }

    private static bool Applied(Reservation reservation, ReservationRevision revision)
    {
        if (revision.Priority is { } priority)
        {
            reservation.Reprioritise(priority);
        }

        if (revision.MarginBefore is not null || revision.MarginAfter is not null)
        {
            reservation.Remargin(
                revision.MarginBefore ?? reservation.MarginBefore,
                revision.MarginAfter ?? reservation.MarginAfter);
        }

        switch (revision.Move)
        {
            case ReservationMove.Cancel:
                reservation.Cancel();

                return false;

            case ReservationMove.Restore:
                reservation.Restore();

                return true;

            default:
                return true;
        }
    }

    private static Reservation[] Touched(IReadOnlyList<Reservation> standing, Reservation? revised)
        => revised is null || standing.Any(held => held.Id.Equals(revised.Id))
            ? [.. standing]
            : [.. standing, revised];

    private static Reservation[] Foreseen(
        IReadOnlyList<Reservation> standing,
        IReadOnlyList<Reservation> joining,
        Reservation? revised)
        => revised is null ? [.. standing, .. joining] : [.. standing, .. joining, revised];

    private static Reservation[] Alongside(
        IReadOnlyList<Reservation> standing,
        IReadOnlyList<Reservation> joining,
        Reservation revised,
        bool stillRunning)
    {
        Reservation[] others = [.. standing.Where(held => !held.Id.Equals(revised.Id))];

        return stillRunning ? [.. others, revised, .. joining] : [.. others, .. joining];
    }

    private static void Apply(AllocationPlan plan, IReadOnlyList<Reservation> considered, DateTime at)
    {
        foreach (Reservation reservation in considered)
        {
            AllocationVerdict verdict = plan.For(reservation.Id).Verdict;

            if (verdict is AllocationVerdict.Unreachable)
            {
                reservation.LoseReception(at);

                continue;
            }

            reservation.RegainReception();

            if (verdict is AllocationVerdict.Contended)
            {
                reservation.Contend();
            }
            else
            {
                reservation.Secure();
            }
        }
    }

    private async Task<Dictionary<ServiceKey, TuningResolution>?> ResolveAsync(
        IReadOnlyList<Reservation> considered,
        CancellationToken cancellationToken)
    {
        Dictionary<ServiceKey, TuningResolution> resolved = [];

        foreach (Reservation reservation in considered)
        {
            ServiceKey key = Naming(reservation);

            if (resolved.ContainsKey(key))
            {
                continue;
            }

            TuningResolution resolution = await tuning.ResolveTuningAsync(
                reservation.NetworkId,
                reservation.ServiceId,
                cancellationToken);

            if (resolution.Refusal is TuningRefusal.LedgerUnreadable)
            {
                return null;
            }

            resolved.Add(key, resolution);
        }

        return resolved;
    }

    private SchedulingRun Weigh(
        IReadOnlyList<Reservation> considered,
        IReadOnlyDictionary<ServiceKey, TuningResolution> selections,
        TunerCapacity capacity,
        DateTime at)
    {
        List<AllocationCandidate> candidates =
        [
            .. considered.Select(reservation =>
                AllocationCandidate.Of(reservation, selections[Naming(reservation)].Tuning)),
        ];

        return SchedulingRun.Of(
            TunerAllocationPlanner.Plan(candidates, capacity, horizon, at),
            capacity.Undetermined.Count);
    }

    private static ServiceKey Naming(Reservation reservation)
        => new(reservation.NetworkId.Value, reservation.ServiceId.Value);

    private static ReservationWindow Reaching(DateTime at)
        => new(at - Margin.Longest, DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc));

    private DateTime Moment() => clock.GetUtcNow().UtcDateTime;

    private readonly record struct ServiceKey(int NetworkId, int ServiceId);
}
