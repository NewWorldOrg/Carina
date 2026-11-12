using Carina.Domain.Base;
using Carina.Domain.Events;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

using Puppis.Contracts;

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
    ReservationOutcomeDomainService ledger,
    IProgrammeRepository programmes,
    BroadcastGroupDomainService groups,
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
            bool movable = reservation.IsYetToBeginAt(at);

            if (resolver.Place(reservation.Programme, reservation.EndAt, at) is not { } placed)
            {
                if (movable
                    && await groups.FallsBackToStandingAloneAsync(reservation, cancellationToken)
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
                if (!await groups.NeedsReservingBesideAsync(sibling, cancellationToken))
                {
                    continue;
                }

                Reservation planned = reservation.PlanBeside(ReservationId.New(), sibling, at);

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
        BroadcastGroupPlacing placing = await groups.PlacingAsync(reservation, own, cancellationToken);

        bool moved = placing.Move switch
        {
            BroadcastGroupMove.Retarget => await RetargetedAsync(reservation, own, at, cancellationToken),
            BroadcastGroupMove.StepAside => await SteppedAsideAsync(reservation, cancellationToken),
            _ => await RegroupedAsync(reservation, placing.Key, placing.Role, cancellationToken),
        };

        return moved ? Made(placing.Move) : Placing.AsItWas;
    }

    private static Placing Made(BroadcastGroupMove move) => move switch
    {
        BroadcastGroupMove.Retarget => Placing.Retargeted,
        BroadcastGroupMove.StepAside => Placing.SteppedAside,
        _ => Placing.Regrouped,
    };

    private async Task<bool> SteppedAsideAsync(Reservation reservation, CancellationToken cancellationToken)
    {
        SchedulingRun run = await scheduling.ReviseAsync(
            reservation,
            new ReservationRevision
            {
                Move = ReservationMove.Cancel,
                Cancellation = ReservationCancellation.SameBroadcast,
            },
            cancellationToken);

        return run.Settled;
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
                        ProgrammeSnapshot.Of(programme, at),
                        EpgComparison.Of(reservation, programme, at),
                        own.Key,
                        own.Role);

                    await reservations.SaveAllAsync([reservation], token);

                    return await ledger.RecordOnceAsync(reservation, ReservationOutcomeKind.ProgrammeMoved, at, token);
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
        if (reservation.IsGroupedAs(key, role))
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
