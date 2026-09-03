using Carina.Domain.Base;
using Carina.Domain.Reservations;

namespace Carina.Infrastructure.Reservations;

public sealed record ReservationOutcomeRecord(ReservationId Reservation, ReservationOutcomeKind Kind);

public sealed record ReservationOutcomeRun(IReadOnlyList<ReservationOutcomeRecord> Recorded);

public sealed class ReservationOutcomeService(
    IReservationRepository reservations,
    IReservationOutcomeRepository outcomes,
    IReservationRecordingContract claims,
    IAtomicWrite write,
    ReservationOutcomeSettings settings,
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

        IReadOnlyList<Reservation> claimed = Contested(judged) is { } contested
            ? await reservations.ListClaimedOverAsync(contested, cancellationToken)
            : [];

        return await write.AllOrNothingAsync(
            async token =>
            {
                List<ReservationOutcomeRecord> recorded = [];
                List<Reservation> moved = [];

                foreach ((Reservation reservation, ReservationOutcomeKind kind) in judged)
                {
                    bool settling = kind is ReservationOutcomeKind.Missed or ReservationOutcomeKind.Competing;

                    // A claim is written in columns the recording ledger owns, so letting go of one
                    // goes through the same statement a refused start uses. Its own condition holds
                    // it back if a recording landed under the claim between the reading and here,
                    // and that recording is then what settles this reservation after all.
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
                            kind is ReservationOutcomeKind.Competing ? Instead(reservation, claimed) : [],
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
    }

    private IReadOnlyList<Judged> Judging(IReadOnlyList<ReservationAwaitingOutcome> awaiting, DateTime at)
        =>
        [
            .. awaiting
                .Select(one => (
                    one.Reservation,
                    Kind: ReservationOutcomeJudgement.Of(one.Reservation, one.Recorded, settings.Grace, at)))
                .Where(pair => pair.Kind is not null)
                .Select(pair => new Judged(pair.Reservation, pair.Kind!.Value))
                .OrderBy(judged => judged.Reservation.EffectiveStartAt)
                .ThenBy(judged => judged.Reservation.Id.Value),
        ];

    private static ReservationWindow? Contested(IReadOnlyList<Judged> judged)
    {
        Judged[] lost = [.. judged.Where(one => one.Kind is ReservationOutcomeKind.Competing)];

        return lost.Length is 0
            ? null
            : new ReservationWindow(
                lost.Min(one => one.Reservation.EffectiveStartAt) - Margin.Longest,
                lost.Max(one => one.Reservation.EffectiveEndAt) + Margin.Longest);
    }

    private static IReadOnlyList<Guid> Instead(Reservation lost, IReadOnlyList<Reservation> claimed)
        =>
        [
            .. claimed
                .Where(won => won.EffectiveStartAt < lost.EffectiveEndAt
                              && lost.EffectiveStartAt < won.EffectiveEndAt)
                .OrderBy(won => won.EffectiveStartAt)
                .ThenBy(won => won.Id.Value)
                .Select(won => won.Id.Value),
        ];

    private readonly record struct Judged(Reservation Reservation, ReservationOutcomeKind Kind);
}
