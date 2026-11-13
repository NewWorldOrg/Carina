using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Unit.Reservations;

[Trait("Category", "Unit")]
public sealed class BroadcastGroupDomainServiceTests
{
    private const int Network = 32001;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime At = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly BroadcastGroupKey Movement = new($"movement:{Network}-1-101");

    private readonly HeldProgrammes programmes = new();

    private readonly HeldReservationLedger reservations = new();

    private BroadcastGroupDomainService Groups => new(reservations, programmes);

    [Fact(DisplayName = "BR-RD-010: a reservation on the listing that stands for its broadcast takes that listing's key and role")]
    public async Task AReservationOnTheStandingListingTakesItsKeyAndRole()
    {
        Programme primary = Listing(1, 101, 60, 120);
        Reservation reservation = Standing(primary);
        var own = new BroadcastTarget(primary, Movement, BroadcastGroupRole.MovementPrimary);

        BroadcastGroupPlacing placing = await Groups.PlacingAsync(reservation, own, Cancel);

        Assert.Equal(BroadcastGroupPlacing.Regrouping(Movement, BroadcastGroupRole.MovementPrimary), placing);
    }

    [Fact(DisplayName = "BR-RD-010: a reservation on a suppressed listing moves onto the primary when nothing stands there")]
    public async Task AReservationOnASuppressedListingMovesOntoThePrimary()
    {
        Programme primary = Listing(1, 101, 60, 120);
        Reservation suppressed = Standing(Listing(2, 201, 90, 150));

        Assert.Equal(
            BroadcastGroupPlacing.Retargeting,
            await Groups.PlacingAsync(suppressed, Primary(primary), Cancel));
    }

    [Theory(DisplayName = "BR-RD-010: a reservation on a suppressed listing steps aside for one recorded, secured or contended on the primary")]
    [InlineData(ReservationState.Scheduled, null)]
    [InlineData(ReservationState.Conflict, null)]
    [InlineData(ReservationState.Cancelled, RecordingOutcome.Complete)]
    public async Task AReservationStepsAsideForOneStandingOnThePrimary(ReservationState state, RecordingOutcome? outcome)
    {
        Programme primary = Listing(1, 101, 60, 120);
        Standing(primary, state, outcome);
        Reservation suppressed = Standing(Listing(2, 201, 90, 150));

        Assert.Equal(
            BroadcastGroupPlacing.SteppingAside,
            await Groups.PlacingAsync(suppressed, Primary(primary), Cancel));
    }

    [Theory(DisplayName = "BR-RD-010: a reservation on a suppressed listing stays there as suppressed when the primary's reservation was cancelled or missed")]
    [InlineData(ReservationState.Cancelled)]
    [InlineData(ReservationState.Missed)]
    public async Task AReservationStaysSuppressedWhenThePrimaryIsNoLongerReserved(ReservationState state)
    {
        Programme primary = Listing(1, 101, 60, 120);
        Standing(primary, state);
        Reservation suppressed = Standing(Listing(2, 201, 90, 150));

        Assert.Equal(
            BroadcastGroupPlacing.Regrouping(Movement, BroadcastGroupRole.MovementSuppressed),
            await Groups.PlacingAsync(suppressed, Primary(primary), Cancel));
    }

    [Fact(DisplayName = "BR-RD-010: a grouped reservation no group places any more stands alone again while the guide holds its programme")]
    public async Task AGroupedReservationStandsAloneAgainWhileItsProgrammeIsHeld()
    {
        Programme held = Listing(1, 101, 60, 120);
        Reservation grouped = Standing(held, role: BroadcastGroupRole.RelaySegment);
        Reservation gone = Planned(Ref(3, 301, 60), BroadcastGroupRole.RelaySegment);

        Assert.True(await Groups.FallsBackToStandingAloneAsync(grouped, Cancel));
        Assert.False(await Groups.FallsBackToStandingAloneAsync(gone, Cancel));
        Assert.False(await Groups.FallsBackToStandingAloneAsync(Standing(Listing(4, 401, 60, 120)), Cancel));
    }

    [Fact(DisplayName = "BR-RD-010: another segment of a relayed broadcast is reserved beside only while nothing stands for it, cancelled included")]
    public async Task AnotherSegmentIsReservedBesideOnlyWhileNothingStandsForIt()
    {
        Programme free = Listing(2, 201, 120, 180);
        Programme cancelled = Listing(3, 301, 180, 240);
        Standing(cancelled, ReservationState.Cancelled);
        var key = new BroadcastGroupKey($"relay:{Network}-1-101");

        Assert.True(await Groups.NeedsReservingBesideAsync(new BroadcastTarget(free, key, BroadcastGroupRole.RelaySegment), Cancel));
        Assert.False(await Groups.NeedsReservingBesideAsync(
            new BroadcastTarget(cancelled, key, BroadcastGroupRole.RelaySegment),
            Cancel));
    }

    private static BroadcastTarget Primary(Programme primary)
        => new(primary, Movement, BroadcastGroupRole.MovementPrimary);

    private static ProgrammeRef Ref(int service, int carried, int startMinutes)
        => new(new NetworkId(Network), new ServiceId(service), new EventId(carried), At.AddMinutes(startMinutes));

    private static Reservation Planned(ProgrammeRef programme, BroadcastGroupRole role)
        => Reservation.Plan(
            ReservationId.New(),
            programme,
            null,
            Priority.Default,
            programme.StartsAt,
            programme.StartsAt.AddHours(1),
            true,
            Margin.None,
            Margin.None,
            ReservationFactory.Snapshot(),
            role is BroadcastGroupRole.Standalone ? null : new BroadcastGroupKey($"relay:{Network}-1-101"),
            role,
            At);

    private Reservation Standing(
        Programme programme,
        ReservationState state = ReservationState.Scheduled,
        RecordingOutcome? outcome = null,
        BroadcastGroupRole role = BroadcastGroupRole.Standalone)
    {
        Reservation reservation = Reservation.Rehydrate(
            ReservationId.New(),
            new ProgrammeRef(programme.NetworkId, programme.ServiceId, programme.EventId, programme.StartsAt),
            null,
            Priority.Default,
            programme.StartsAt,
            programme.EndsAt!.Value,
            true,
            Margin.None,
            Margin.None,
            ProgrammeSnapshot.Of(programme, At),
            role is BroadcastGroupRole.Standalone ? null : new BroadcastGroupKey($"relay:{Network}-1-101"),
            role,
            state,
            outcome is null ? null : At,
            outcome,
            false,
            [],
            false,
            null,
            false,
            null,
            At,
            state is ReservationState.Cancelled ? ReservationCancellation.ByHand : null);
        reservations.Reservations.Add(reservation);

        return reservation;
    }

    private Programme Listing(int service, int carried, int startMinutes, int endMinutes)
    {
        Programme programme = Programme.Rehydrate(
            new ProgrammeId(new NetworkId(Network), new ServiceId(service), new EventId(carried)),
            new TransportStreamId(Network),
            At.AddMinutes(startMinutes),
            At.AddMinutes(endMinutes),
            $"番組 {service}-{carried}",
            string.Empty,
            false,
            At);
        programmes.Programmes.Add(programme);

        return programme;
    }
}
