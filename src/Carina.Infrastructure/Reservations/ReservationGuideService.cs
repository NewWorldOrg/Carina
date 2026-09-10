using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Events;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

namespace Carina.Infrastructure.Reservations;

public sealed record GuideRun(
    IReadOnlyList<ReservationId> Followed,
    IReadOnlyList<ReservationId> Cancelled);

/// <summary>
/// Holds every reservation still ahead against what the guide says now. A broadcast stays the same
/// broadcast because the guide names it — network, service and event — so a renamed programme at a
/// different hour is still the one that was reserved, and the reservation follows it there. A
/// broadcast the guide no longer announces takes its reservation out of the running.
///
/// Nothing here reads "the guide does not hold it" as "it is not broadcast". A programme row is
/// never taken away while the broadcast is still ahead, and the row's own timestamp stands still
/// for as long as nothing about the programme changes, so neither of them separates a broadcast
/// that was dropped from one the guide was simply never read far enough to carry. What separates
/// them is the mark a reading leaves on the programmes it named: a reading that heard the whole of
/// a service's announced schedule marks every programme it named, so a programme of that service
/// carrying an older mark than the service's newest one was offered for reading and was not there.
/// A service no reading has ever heard whole says nothing, and nothing is what is done about it.
/// </summary>
public sealed class ReservationGuideService(
    IReservationRepository reservations,
    IReservationOutcomeRepository outcomes,
    IProgrammeRepository programmes,
    ReservationSchedulingService scheduling,
    IAtomicWrite write,
    IAppEventPublisher events,
    TimeProvider clock)
{
    public async Task<GuideRun> ReconcileAsync(CancellationToken cancellationToken)
    {
        DateTime at = clock.GetUtcNow().UtcDateTime;
        IReadOnlyList<Reservation> ahead = await reservations.ListPendingAsync(Everything(at), cancellationToken);

        var moved = new List<Moved>();
        var gone = new List<Reservation>();

        foreach (Reservation reservation in ahead)
        {
            if (!NotYetUnderWay(reservation, at))
            {
                continue;
            }

            Reading reading = await ReadAsync(reservation, at, cancellationToken);

            if (reading.Gone)
            {
                gone.Add(reservation);
            }
            else if (reading.Announced is { } announced && reading.Divergences.Count > 0)
            {
                moved.Add(new Moved(reservation, announced, reading.Divergences));
            }
        }

        IReadOnlyList<ReservationId> followed = await FollowAsync(moved, at, cancellationToken);
        IReadOnlyList<ReservationId> cancelled = await CancelAsync(gone, at, cancellationToken);

        if (followed.Count > 0 || cancelled.Count > 0)
        {
            events.Signal(AppEventName.Reservations);
        }

        return new GuideRun(followed, cancelled);
    }

    private async Task<Reading> ReadAsync(
        Reservation reservation,
        DateTime at,
        CancellationToken cancellationToken)
    {
        DateTime? heardWholeAt = await programmes.HeardWholeAtAsync(
            reservation.NetworkId.Value,
            reservation.ServiceId.Value,
            cancellationToken);

        if (await programmes.FindAsync(reservation.Programme.Id, cancellationToken) is not { } announced)
        {
            return heardWholeAt is null ? Reading.Nothing : Reading.Vanished;
        }

        if (heardWholeAt is { } whole && announced.LastHeardAt is { } named && named < whole)
        {
            return Reading.Vanished;
        }

        return announced.IsShadow
            ? Reading.Nothing
            : new Reading(false, announced, EpgComparison.Of(reservation, announced, at));
    }

    private async Task<IReadOnlyList<ReservationId>> FollowAsync(
        IReadOnlyList<Moved> moved,
        DateTime at,
        CancellationToken cancellationToken)
    {
        if (moved.Count is 0)
        {
            return [];
        }

        return await write.AllOrNothingAsync(
            async token =>
            {
                var followed = new List<ReservationId>();
                var touched = new List<Reservation>();

                foreach ((Reservation reservation, Programme announced, IReadOnlyList<EpgDivergence> divergences)
                    in moved)
                {
                    reservation.Follow(
                        announced.StartsAt,
                        EpgComparison.EndOf(announced),
                        announced.EndsAt is not null,
                        ProgrammeSnapshot.Of(
                            announced.Name,
                            announced.Summary,
                            announced.Items,
                            announced.Genres,
                            at),
                        divergences);

                    touched.Add(reservation);
                    followed.Add(reservation.Id);

                    await ReservationLedger.WriteOnceAsync(
                        outcomes,
                        reservation,
                        ReservationOutcomeKind.ProgrammeMoved,
                        at,
                        token);
                }

                await reservations.SaveAllAsync(touched, token);

                return (IReadOnlyList<ReservationId>)followed;
            },
            cancellationToken);
    }

    /// <summary>
    /// Taking a reservation out of the running is the allocation's move to make, so it is made
    /// there — the same call the person pressing cancel goes through — and what is left over is
    /// only the mark saying why and the line in the ledger. An allocation that could not be settled
    /// leaves the reservation exactly as it was, and the next pass reads the guide again.
    /// </summary>
    private async Task<IReadOnlyList<ReservationId>> CancelAsync(
        IReadOnlyList<Reservation> gone,
        DateTime at,
        CancellationToken cancellationToken)
    {
        var cancelled = new List<ReservationId>();

        foreach (Reservation reservation in gone)
        {
            SchedulingRun run = await scheduling.ReviseAsync(
                reservation,
                new ReservationRevision
                {
                    Move = ReservationMove.Cancel,
                    Cancellation = ReservationCancellation.ProgrammeGone,
                },
                cancellationToken);

            if (!run.Settled)
            {
                continue;
            }

            await write.AllOrNothingAsync(
                async token =>
                {
                    reservation.Disappear();

                    await reservations.SaveAllAsync([reservation], token);

                    return await ReservationLedger.WriteOnceAsync(
                        outcomes,
                        reservation,
                        ReservationOutcomeKind.ProgrammeGone,
                        at,
                        token);
                },
                cancellationToken);

            cancelled.Add(reservation.Id);
        }

        return cancelled;
    }

    private static bool NotYetUnderWay(Reservation reservation, DateTime at)
        => !reservation.IsPinned && reservation.EffectiveStartAt > at;

    private static ReservationWindow Everything(DateTime at)
        => new(at, DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc));

    private readonly record struct Moved(
        Reservation Reservation,
        Programme Announced,
        IReadOnlyList<EpgDivergence> Divergences);

    private readonly record struct Reading(
        bool Gone,
        Programme? Announced,
        IReadOnlyList<EpgDivergence> Divergences)
    {
        public static readonly Reading Nothing = new(false, null, []);

        public static readonly Reading Vanished = new(true, null, []);
    }
}
