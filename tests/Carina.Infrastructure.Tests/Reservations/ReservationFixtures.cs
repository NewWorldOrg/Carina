using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;

namespace Carina.Infrastructure.Tests.Reservations;

internal static class ReservationFixtures
{
    public static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private static int nextEvent = 5000;

    public static int NextEventId() => Interlocked.Increment(ref nextEvent);

    public static ProgrammeRef Programme(int eventId, int serviceId = 1024, DateTime? startsAt = null)
        => new(
            new NetworkId(32736),
            new ServiceId(serviceId),
            new EventId(eventId),
            startsAt ?? Now.AddHours(2));

    public static ProgrammeSnapshot Snapshot()
        => new("A programme", "What it is about", string.Empty, [new ProgrammeGenre(7, 1)], Now);

    public static Reservation Planned(
        ProgrammeRef? programme = null,
        Priority? priority = null,
        RuleId? ruleId = null,
        DateTime? startAt = null,
        DateTime? endAt = null,
        bool endAtConfirmed = true,
        Margin? marginBefore = null,
        Margin? marginAfter = null,
        BroadcastGroupKey? groupKey = null,
        BroadcastGroupRole groupRole = BroadcastGroupRole.Standalone)
    {
        ProgrammeRef reference = programme ?? Programme(NextEventId());
        DateTime opens = startAt ?? reference.StartsAt;

        return Reservation.Plan(
            ReservationId.New(),
            reference,
            ruleId,
            priority ?? Priority.Default,
            opens,
            endAt ?? opens.AddHours(1),
            endAtConfirmed,
            marginBefore ?? Margin.None,
            marginAfter ?? Margin.None,
            Snapshot(),
            groupKey,
            groupRole,
            Now);
    }

    public static Reservation Rehydrated(
        ReservationState state,
        DateTime? startedAt = null,
        RecordingOutcome? outcome = null,
        ProgrammeRef? programme = null,
        Priority? priority = null,
        DateTime? startAt = null,
        DateTime? endAt = null,
        bool endAtConfirmed = true,
        Margin? marginBefore = null,
        Margin? marginAfter = null,
        bool receptionUnavailable = false,
        DateTime? receptionUnavailableSince = null)
    {
        ProgrammeRef reference = programme ?? Programme(NextEventId());
        DateTime opens = startAt ?? reference.StartsAt;

        return Reservation.Rehydrate(
            ReservationId.New(),
            reference,
            null,
            priority ?? Priority.Default,
            opens,
            endAt ?? opens.AddHours(1),
            endAtConfirmed,
            marginBefore ?? Margin.None,
            marginAfter ?? Margin.None,
            Snapshot(),
            null,
            BroadcastGroupRole.Standalone,
            state,
            startedAt,
            outcome,
            false,
            [],
            false,
            null,
            receptionUnavailable,
            receptionUnavailableSince,
            Now);
    }
}
