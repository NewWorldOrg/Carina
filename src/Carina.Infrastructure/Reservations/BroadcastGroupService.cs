using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Events;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

namespace Carina.Infrastructure.Reservations;

public sealed record BroadcastGroupRun(
    IReadOnlyList<ReservationId> Retargeted,
    IReadOnlyList<ReservationId> SteppedAside,
    IReadOnlyList<ReservationId> Regrouped,
    IReadOnlyList<Reservation> Made);

/// <summary>
/// Holds every reservation still standing against the relayed and moved broadcasts the guide links.
/// </summary>
/// <remarks>
/// A reservation on a listing a moved broadcast suppresses is moved onto the primary listing, or
/// cancelled when another reservation already stands there. A reservation on a segment of a relayed
/// broadcast has the segments still to come reserved beside it, after its own example. A reservation
/// already holding a tuner is never moved or regrouped, and still sets the example for the segments
/// after it.
/// </remarks>
public sealed class BroadcastGroupService(
    IReservationRepository reservations,
    IReservationOutcomeRepository outcomes,
    IProgrammeRepository programmes,
    ReservationSchedulingService scheduling,
    IAtomicWrite write,
    IAppEventPublisher events,
    TimeProvider clock)
{
    public async Task<BroadcastGroupRun> ReconcileAsync(CancellationToken cancellationToken)
    {
        DateTime at = clock.GetUtcNow().UtcDateTime;
        BroadcastGroupResolver resolver = BroadcastGroupResolver.Of(await programmes.ListGroupedAsync(cancellationToken));
        IReadOnlyList<Reservation> standing = await reservations.ListPendingAsync(Everything(at), cancellationToken);

        List<ReservationId> retargeted = [];
        List<ReservationId> steppedAside = [];
        List<ReservationId> regrouped = [];
        List<Reservation> made = [];

        foreach (Reservation reservation in standing)
        {
            bool movable = !reservation.IsPinned && reservation.EffectiveStartAt > at;

            if (resolver.Place(reservation.Programme, reservation.EndAt, at) is not { } placed)
            {
                if (movable
                    && reservation.BroadcastGroupRole is not BroadcastGroupRole.Standalone
                    && await programmes.FindAsync(reservation.Programme.Id, cancellationToken) is not null
                    && await RegroupedAsync(reservation, null, BroadcastGroupRole.Standalone, cancellationToken))
                {
                    regrouped.Add(reservation.Id);
                }

                continue;
            }

            if (movable)
            {
                switch (await PlaceAsync(reservation, placed.Own, at, cancellationToken))
                {
                    case Placing.Retargeted:
                        retargeted.Add(reservation.Id);
                        break;
                    case Placing.SteppedAside:
                        steppedAside.Add(reservation.Id);
                        continue;
                    case Placing.Regrouped:
                        regrouped.Add(reservation.Id);
                        break;
                }
            }

            foreach (BroadcastTarget sibling in placed.Alongside)
            {
                if (await reservations.FindByProgrammeAsync(sibling.Reference, cancellationToken) is not null)
                {
                    continue;
                }

                Reservation planned = Beside(reservation, sibling, at);

                if ((await scheduling.CreateAsync(planned, cancellationToken)).Settled)
                {
                    made.Add(planned);
                }
            }
        }

        if (retargeted.Count > 0 || regrouped.Count > 0)
        {
            events.Signal(AppEventName.Reservations);
        }

        return new BroadcastGroupRun(retargeted, steppedAside, regrouped, made);
    }

    private async Task<Placing> PlaceAsync(
        Reservation reservation,
        BroadcastTarget own,
        DateTime at,
        CancellationToken cancellationToken)
    {
        if (own.Programme.Id.Equals(reservation.Programme.Id))
        {
            return await RegroupedAsync(reservation, own.Key, own.Role, cancellationToken)
                ? Placing.Regrouped
                : Placing.AsItWas;
        }

        Reservation? there = await reservations.FindByProgrammeAsync(own.Reference, cancellationToken);

        if (there is null)
        {
            return await RetargetedAsync(reservation, own, at, cancellationToken)
                ? Placing.Retargeted
                : Placing.AsItWas;
        }

        if (there.RecordingOutcome is not null
            || there.State is ReservationState.Scheduled or ReservationState.Conflict)
        {
            SchedulingRun run = await scheduling.ReviseAsync(
                reservation,
                new ReservationRevision
                {
                    Move = ReservationMove.Cancel,
                    Cancellation = ReservationCancellation.SameBroadcast,
                },
                cancellationToken);

            return run.Settled ? Placing.SteppedAside : Placing.AsItWas;
        }

        return await RegroupedAsync(reservation, own.Key, BroadcastGroupRole.MovementSuppressed, cancellationToken)
            ? Placing.Regrouped
            : Placing.AsItWas;
    }

    private async Task<bool> RetargetedAsync(
        Reservation reservation,
        BroadcastTarget own,
        DateTime at,
        CancellationToken cancellationToken)
    {
        Programme programme = own.Programme;

        try
        {
            await write.AllOrNothingAsync(
                async token =>
                {
                    reservation.Retarget(
                        own.Reference,
                        EpgComparison.EndOf(programme),
                        programme.EndsAt is not null,
                        Snapshot(programme, at),
                        EpgComparison.Of(reservation, programme, at),
                        own.Key,
                        own.Role);

                    await reservations.SaveAllAsync([reservation], token);

                    return await ReservationLedger.WriteOnceAsync(
                        outcomes,
                        reservation,
                        ReservationOutcomeKind.ProgrammeMoved,
                        at,
                        token);
                },
                cancellationToken);
        }
        catch (ReservationMovedMeanwhileException)
        {
            return false;
        }

        return true;
    }

    private async Task<bool> RegroupedAsync(
        Reservation reservation,
        BroadcastGroupKey? key,
        BroadcastGroupRole role,
        CancellationToken cancellationToken)
    {
        if (Equals(reservation.BroadcastGroupKey, key) && reservation.BroadcastGroupRole == role)
        {
            return false;
        }

        try
        {
            await write.AllOrNothingAsync(
                async token =>
                {
                    reservation.Regroup(key, role);

                    await reservations.SaveAllAsync([reservation], token);

                    return true;
                },
                cancellationToken);
        }
        catch (ReservationMovedMeanwhileException)
        {
            return false;
        }

        return true;
    }

    private static Reservation Beside(Reservation example, BroadcastTarget sibling, DateTime at)
    {
        Programme programme = sibling.Programme;

        return Reservation.Plan(
            ReservationId.New(),
            sibling.Reference,
            example.RuleId,
            example.Priority,
            programme.StartsAt,
            EpgComparison.EndOf(programme),
            programme.EndsAt is not null,
            example.MarginBefore,
            example.MarginAfter,
            Snapshot(programme, at),
            sibling.Key,
            sibling.Role,
            at,
            example.EncodeWhenRecorded);
    }

    private static ProgrammeSnapshot Snapshot(Programme programme, DateTime at)
        => ProgrammeSnapshot.Of(
            programme.Name,
            programme.Summary,
            programme.Items,
            programme.Genres,
            at,
            programme.Audio,
            programme.Sounds);

    private static ReservationWindow Everything(DateTime at)
        => new(at, DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc));

    private enum Placing
    {
        AsItWas = 0,

        Retargeted = 1,

        SteppedAside = 2,

        Regrouped = 3,
    }
}
