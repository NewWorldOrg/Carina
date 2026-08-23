using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;

namespace Carina.Domain.Tests.Reservations;

internal static class ReservationFactory
{
    public static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    public static ProgrammeRef Programme(int eventId = 4001, DateTime? startsAt = null)
        => new(new NetworkId(32736), new ServiceId(1024), new EventId(eventId), startsAt ?? Now.AddHours(2));

    public static ProgrammeSnapshot Snapshot()
        => new("A programme", "What it is about", string.Empty, [new ProgrammeGenre(7, 1)], Now);

    public static Reservation Planned(
        RuleId? ruleId = null,
        Priority? priority = null,
        ProgrammeRef? programme = null,
        Margin? marginBefore = null,
        Margin? marginAfter = null)
    {
        ProgrammeRef reference = programme ?? Programme();

        return Reservation.Plan(
            ReservationId.New(),
            reference,
            ruleId,
            priority ?? Priority.Default,
            reference.StartsAt,
            reference.StartsAt.AddHours(1),
            true,
            marginBefore ?? Margin.None,
            marginAfter ?? Margin.None,
            Snapshot(),
            null,
            BroadcastGroupRole.Standalone,
            Now);
    }

    public static Reservation Claimed(DateTime? at = null)
        => Rehydrated(ReservationState.Scheduled, at ?? Now, null);

    public static Reservation Rehydrated(
        ReservationState state,
        DateTime? startedAt,
        RecordingOutcome? outcome,
        BroadcastGroupKey? groupKey = null,
        BroadcastGroupRole groupRole = BroadcastGroupRole.Standalone,
        bool epgDiverged = false,
        IReadOnlyList<EpgDivergence>? divergences = null,
        bool epgMissing = false,
        DateTime? acknowledgedAt = null)
    {
        ProgrammeRef programme = Programme();

        return Reservation.Rehydrate(
            ReservationId.New(),
            programme,
            null,
            Priority.Default,
            programme.StartsAt,
            programme.StartsAt.AddHours(1),
            true,
            Margin.None,
            Margin.None,
            Snapshot(),
            groupKey,
            groupRole,
            state,
            startedAt,
            outcome,
            epgDiverged,
            divergences ?? [],
            epgMissing,
            acknowledgedAt,
            Now);
    }
}
