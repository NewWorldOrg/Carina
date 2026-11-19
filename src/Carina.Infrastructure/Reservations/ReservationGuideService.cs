using Carina.Domain.Base;
using Carina.Domain.Events;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

using Puppis.Contracts;

namespace Carina.Infrastructure.Reservations;

public sealed record GuideRun(
    IReadOnlyList<ReservationId> Followed,
    IReadOnlyList<ReservationId> Cancelled);

public sealed class ReservationGuideService(
    IReservationRepository reservations,
    ReservationOutcomeDomainService ledger,
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
            if (!reservation.IsYetToBeginAt(at))
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
        Programme? announced = await programmes.FindAsync(reservation.Programme.Id, cancellationToken);

        GuideStanding standing = GuideReading.Of(announced, heardWholeAt);

        if (standing is GuideStanding.NoLongerAnnounced)
        {
            return Reading.Vanished;
        }

        return standing is GuideStanding.Announced && announced is not null
            ? new Reading(false, announced, EpgComparison.Of(reservation, announced, at))
            : Reading.Nothing;
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
                        ProgrammeSnapshot.Of(announced, at),
                        divergences);

                    touched.Add(reservation);
                    followed.Add(reservation.Id);

                    await ledger.RecordOnceAsync(reservation, ReservationOutcomeKind.ProgrammeMoved, at, token);
                }

                await reservations.SaveAllAsync(touched, token);

                return (IReadOnlyList<ReservationId>)followed;
            },
            cancellationToken);
    }

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

                    return await ledger.RecordOnceAsync(reservation, ReservationOutcomeKind.ProgrammeGone, at, token);
                },
                cancellationToken);

            cancelled.Add(reservation.Id);
        }

        return cancelled;
    }

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
