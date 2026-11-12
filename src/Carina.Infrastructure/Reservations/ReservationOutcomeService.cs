using Carina.Domain.Base;
using Carina.Domain.Events;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

using Puppis.Contracts;

namespace Carina.Infrastructure.Reservations;

public sealed record ReservationOutcomeRecord(ReservationId Reservation, ReservationOutcomeKind Kind);

public sealed record ReservationOutcomeRun(IReadOnlyList<ReservationOutcomeRecord> Recorded);

public sealed class ReservationOutcomeService(
    IReservationRepository reservations,
    IReservationOutcomeRepository outcomes,
    IReservationRecordingContract claims,
    ReservationOutcomeDomainService ledger,
    IAtomicWrite write,
    ReservationOutcomeSettings settings,
    IAppEventPublisher events,
    TimeProvider clock)
{
    public async Task<ReservationOutcomeRun> RecordAsync(CancellationToken cancellationToken)
    {
        DateTime at = clock.GetUtcNow().UtcDateTime;
        IReadOnlyList<Judged> judged = Judging(
            await reservations.ListAwaitingOutcomeAsync(at, cancellationToken),
            at);

        if (judged.Count is 0)
        {
            return new ReservationOutcomeRun([]);
        }

        Reservation[] lost = [.. judged.Where(one => one.Kind is ReservationOutcomeKind.Competing).Select(one => one.Reservation)];
        IReadOnlyList<Reservation> claimed = lost.Length is 0
            ? []
            : await reservations.ListClaimedOverAsync(RecordedInstead.LookedForAcross(lost), cancellationToken);

        ReservationOutcomeRun run = await write.AllOrNothingAsync(
            async token =>
            {
                List<ReservationOutcomeRecord> recorded = [];
                List<Reservation> moved = [];

                foreach ((Reservation reservation, ReservationOutcomeKind kind) in judged)
                {
                    bool settling = kind is ReservationOutcomeKind.Missed or ReservationOutcomeKind.Competing;

                    if (settling
                        && reservation.StartedAt is { } claimedAt
                        && !await claims.ReleaseAsync(reservation.Id, claimedAt, token))
                    {
                        continue;
                    }

                    await outcomes.AddAsync(
                        ReservationOutcome.Record(
                            ReservationOutcomeId.New(),
                            reservation,
                            kind,
                            null,
                            kind is ReservationOutcomeKind.RecordingFailure ? reservation.RecordingOutcome : null,
                            kind is ReservationOutcomeKind.RecordingFailure
                                ? await ledger.WhyItFailedAsync(reservation.Id, token)
                                : [],
                            kind is ReservationOutcomeKind.Competing ? RecordedInstead.Of(reservation, claimed) : [],
                            at),
                        token);

                    if (settling)
                    {
                        if (reservation.IsPinned)
                        {
                            reservation.Abandon();
                        }
                        else
                        {
                            reservation.Miss();
                        }

                        moved.Add(reservation);
                    }

                    recorded.Add(new ReservationOutcomeRecord(reservation.Id, kind));
                }

                if (moved.Count > 0)
                {
                    await reservations.SaveAllAsync(moved, token);
                }

                return new ReservationOutcomeRun(recorded);
            },
            cancellationToken);

        if (run.Recorded.Count > 0)
        {
            events.Signal(AppEventName.Reservations);
        }

        return run;
    }

    private IReadOnlyList<Judged> Judging(IReadOnlyList<ReservationAwaitingOutcome> awaiting, DateTime at)
        =>
        [
            .. awaiting
                .Select(one => (
                    one.Reservation,
                    Kind: ReservationOutcomeJudgement.Of(
                        one.Reservation,
                        one.Recorded,
                        one.LeftScrambled,
                        settings.Grace,
                        at)))
                .Where(pair => pair.Kind is not null)
                .Select(pair => new Judged(pair.Reservation, pair.Kind!.Value))
                .OrderBy(judged => judged.Reservation.EffectiveStartAt)
                .ThenBy(judged => judged.Reservation.Id.Value),
        ];

    private readonly record struct Judged(Reservation Reservation, ReservationOutcomeKind Kind);
}
