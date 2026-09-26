using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Events;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Infrastructure.Reservations;

public sealed class ReservationSchedulingService(
    IReservationRepository reservations,
    IRecordingRepository recordings,
    ITunerCapacityDirectory seating,
    IServiceTuningDirectory tuning,
    IAtomicWrite write,
    RollingHorizon horizon,
    IAppEventPublisher events,
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
        IReadOnlyDictionary<ReservationId, DateTime> held = await HeldUntilAsync(cancellationToken);

        return await ResolveAsync(considered, cancellationToken) is { } selections
            ? Weigh(considered, selections, capacity, at, held)
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
        IReadOnlyDictionary<ReservationId, DateTime> held = await HeldUntilAsync(cancellationToken);

        if (await ResolveAsync(Foreseen(looked, joining, revised), cancellationToken) is not { } selections)
        {
            return SchedulingRun.Refused(SchedulingRefusal.CapacityUnknown);
        }

        int moved = 0;

        SchedulingRun written;

        try
        {
            written = await write.AllOrNothingAsync(
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

                    SchedulingRun run = Weigh(considered, selections, capacity, at, held);

                    if (!run.Settled)
                    {
                        return run;
                    }

                    moved = Apply(run.Plan, considered, at);

                    await reservations.SaveAllAsync(Touched(standing, revised), token);

                    foreach (Reservation joined in joining)
                    {
                        await reservations.AddAsync(joined, token);
                    }

                    return run;
                },
                cancellationToken);
        }
        catch (ReservationMovedMeanwhileException)
        {
            return SchedulingRun.Refused(SchedulingRefusal.SomethingArrivedWhileReading);
        }

        if (written.Settled && (moved > 0 || joining.Count > 0 || revised is not null))
        {
            events.Signal(AppEventName.Reservations);
        }

        return written;
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

        if (revision.EncodeWhenRecorded is { } encodeWhenRecorded)
        {
            reservation.Rewish(encodeWhenRecorded);
        }

        switch (revision.Move)
        {
            case ReservationMove.Cancel:
                reservation.Cancel(revision.Cancellation);

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

    private static int Apply(AllocationPlan plan, IReadOnlyList<Reservation> considered, DateTime at)
    {
        int moved = 0;

        foreach (Reservation reservation in considered)
        {
            ReservationState stood = reservation.State;
            bool unreachable = reservation.ReceptionUnavailable;
            AllocationVerdict verdict = plan.For(reservation.Id).Verdict;

            if (verdict is AllocationVerdict.Unreachable)
            {
                reservation.LoseReception(at);
            }
            else
            {
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

            if (reservation.State != stood || reservation.ReceptionUnavailable != unreachable)
            {
                moved++;
            }
        }

        return moved;
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
        DateTime at,
        IReadOnlyDictionary<ReservationId, DateTime> held)
    {
        List<AllocationCandidate> candidates =
        [
            .. considered.Select(reservation =>
                AllocationCandidate.Of(
                    reservation,
                    selections[Naming(reservation)].Tuning,
                    held.TryGetValue(reservation.Id, out DateTime until) ? until : null)),
        ];

        return SchedulingRun.Of(
            TunerAllocationPlanner.Plan(candidates, capacity, horizon, at),
            capacity.Undetermined.Count);
    }

    /// <summary>
    /// How far the recordings that are already running are actually promised, read against the
    /// reservation each of them belongs to. A recording that followed its programme past the end
    /// its reservation still names holds its tuner until the window it was granted, and the
    /// reservation row says nothing about that: without this the plan would seat the next
    /// reservation on a tuner that is not free yet.
    /// </summary>
    private async Task<IReadOnlyDictionary<ReservationId, DateTime>> HeldUntilAsync(
        CancellationToken cancellationToken)
    {
        Dictionary<ReservationId, DateTime> held = [];

        foreach (Recording recording in await recordings.ListInFlightAsync(cancellationToken))
        {
            if (recording.ReservationId is not { } reservation)
            {
                continue;
            }

            if (!held.TryGetValue(reservation, out DateTime standing)
                || recording.ExpectedWindowEnd > standing)
            {
                held[reservation] = recording.ExpectedWindowEnd;
            }
        }

        return held;
    }

    private static ServiceKey Naming(Reservation reservation)
        => new(reservation.NetworkId.Value, reservation.ServiceId.Value);

    private static ReservationWindow Reaching(DateTime at)
        => new(at - Margin.Longest, DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc));

    private DateTime Moment() => clock.GetUtcNow().UtcDateTime;

    private readonly record struct ServiceKey(int NetworkId, int ServiceId);
}
