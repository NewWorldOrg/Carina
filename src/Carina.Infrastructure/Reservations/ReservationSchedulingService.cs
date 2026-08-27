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

        return SettleAsync([reservation], cancellationToken);
    }

    public Task<SchedulingRun> RecalculateAsync(CancellationToken cancellationToken)
        => SettleAsync([], cancellationToken);

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

        return await WeighAsync([.. standing, .. proposed], capacity, at, cancellationToken);
    }

    private async Task<SchedulingRun> SettleAsync(
        IReadOnlyList<Reservation> joining,
        CancellationToken cancellationToken)
    {
        DateTime at = Moment();

        if (await seating.ReadAsync(cancellationToken) is not { } capacity)
        {
            return SchedulingRun.Refused(SchedulingRefusal.CapacityUnknown);
        }

        return await write.AllOrNothingAsync(
            async token =>
            {
                IReadOnlyList<Reservation> standing = await reservations.ListPendingAsync(Reaching(at), token);
                Reservation[] considered = [.. standing, .. joining];
                SchedulingRun run = await WeighAsync(considered, capacity, at, token);

                if (!run.Settled)
                {
                    return run;
                }

                Apply(run.Plan, considered, at);

                await reservations.SaveAllAsync(standing, token);

                foreach (Reservation joined in joining)
                {
                    await reservations.AddAsync(joined, token);
                }

                return run;
            },
            cancellationToken);
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

    private async Task<SchedulingRun> WeighAsync(
        IReadOnlyList<Reservation> considered,
        TunerCapacity capacity,
        DateTime at,
        CancellationToken cancellationToken)
    {
        Dictionary<ServiceKey, TuningResolution> resolved = [];
        List<AllocationCandidate> candidates = [];

        foreach (Reservation reservation in considered)
        {
            ServiceKey key = new(reservation.NetworkId.Value, reservation.ServiceId.Value);

            if (!resolved.TryGetValue(key, out TuningResolution? resolution))
            {
                resolution = await tuning.ResolveTuningAsync(
                    reservation.NetworkId,
                    reservation.ServiceId,
                    cancellationToken);

                resolved.Add(key, resolution);
            }

            if (resolution.Refusal is TuningRefusal.CapacityUnknown)
            {
                return SchedulingRun.Refused(SchedulingRefusal.CapacityUnknown);
            }

            candidates.Add(AllocationCandidate.Of(reservation, resolution.Tuning));
        }

        return SchedulingRun.Of(TunerAllocationPlanner.Plan(candidates, capacity, horizon, at));
    }

    private static ReservationWindow Reaching(DateTime at)
        => new(at - Margin.Longest, DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc));

    private DateTime Moment() => clock.GetUtcNow().UtcDateTime;

    private readonly record struct ServiceKey(int NetworkId, int ServiceId);
}
