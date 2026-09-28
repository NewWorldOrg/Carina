using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;

namespace Carina.Domain.Tests.Reservations;

public sealed class ReservationRetargetTests
{
    private static readonly DateTime Later = ReservationFactory.Now.AddMinutes(5);

    private static readonly BroadcastGroupKey Key = new("movement:32736-1024-4001");

    [Fact]
    public void BrRd010AReservationMovedOntoThePrimaryKeepsWhatItWasGiven()
    {
        RuleId rule = RuleId.New();
        Reservation booked = ReservationFactory.Planned(
            ruleId: rule,
            priority: new Priority(40),
            marginBefore: Margin.OfSeconds(60),
            marginAfter: Margin.OfSeconds(120));
        ReservationId id = booked.Id;
        DateTime created = booked.CreatedAt;
        ProgrammeRef primary = Elsewhere(booked.ProgrammeStartsAt.AddMinutes(-10));

        booked.Retarget(primary, primary.StartsAt.AddHours(2), true, Snapshot(), [Carried()], Key, BroadcastGroupRole.MovementPrimary);

        Assert.Equal(id, booked.Id);
        Assert.Equal(rule, booked.RuleId);
        Assert.Equal(new Priority(40), booked.Priority);
        Assert.Equal(Margin.OfSeconds(60), booked.MarginBefore);
        Assert.Equal(Margin.OfSeconds(120), booked.MarginAfter);
        Assert.Equal(created, booked.CreatedAt);
        Assert.Equal(primary, booked.Programme);
        Assert.Equal(primary.StartsAt, booked.StartAt);
        Assert.Equal(primary.StartsAt.AddHours(2), booked.EndAt);
        Assert.Equal("The primary listing", booked.SnapshotName);
        Assert.Equal(Key, booked.BroadcastGroupKey);
        Assert.Equal(BroadcastGroupRole.MovementPrimary, booked.BroadcastGroupRole);
        Assert.True(booked.EpgDiverged);
        Assert.Equal(DivergedField.Service, Assert.Single(booked.EpgDivergences).Field);
    }

    [Fact]
    public void BrRd010AReservationMovedOntoAListingWhoseOwnHadLeftTheGuideIsNoLongerMarkedMissing()
    {
        Reservation booked = ReservationFactory.Planned();
        booked.Disappear();
        booked.Acknowledge(Later);
        ProgrammeRef primary = Elsewhere(booked.ProgrammeStartsAt);

        booked.Retarget(primary, primary.StartsAt.AddHours(1), true, Snapshot(), [], Key, BroadcastGroupRole.MovementPrimary);

        Assert.False(booked.EpgMissing);
        Assert.Null(booked.AcknowledgedAt);
    }

    [Fact]
    public void BrRd010AReservationAlreadyHoldingATunerIsNotMovedOntoAnotherListing()
    {
        Reservation recording = ReservationFactory.Claimed();

        Assert.Throws<InvalidOperationException>(() => recording.Retarget(
            Elsewhere(recording.ProgrammeStartsAt),
            recording.EndAt,
            true,
            Snapshot(),
            [Carried()],
            Key,
            BroadcastGroupRole.MovementPrimary));
    }

    [Fact]
    public void BrRd010ACancelledReservationIsNotMovedOntoAnotherListing()
    {
        Reservation cancelled = ReservationFactory.Rehydrated(ReservationState.Cancelled, null, null);

        Assert.Throws<InvalidOperationException>(() => cancelled.Retarget(
            Elsewhere(cancelled.ProgrammeStartsAt),
            cancelled.EndAt,
            true,
            Snapshot(),
            [Carried()],
            Key,
            BroadcastGroupRole.MovementPrimary));
    }

    [Fact]
    public void BrRd010AReservationMovedIntoAGroupNamesTheGroup()
    {
        Reservation booked = ReservationFactory.Planned();

        Assert.Throws<ArgumentException>(() => booked.Retarget(
            Elsewhere(booked.ProgrammeStartsAt),
            booked.EndAt,
            true,
            Snapshot(),
            [Carried()],
            null,
            BroadcastGroupRole.MovementPrimary));
    }

    [Fact]
    public void BrRd010AReservationStandingAsideForTheSameBroadcastIsCancelledForThatReason()
    {
        Reservation booked = ReservationFactory.Planned();

        booked.Cancel(ReservationCancellation.SameBroadcast);

        Assert.Equal(ReservationState.Cancelled, booked.State);
        Assert.Equal(ReservationCancellation.SameBroadcast, booked.Cancellation);
    }

    private static ProgrammeRef Elsewhere(DateTime startsAt)
        => new(new NetworkId(32736), new ServiceId(1025), new EventId(5001), startsAt);

    private static EpgDivergence Carried()
        => new(DivergedField.Service, "32736-1024", "32736-1025", Later);

    private static ProgrammeSnapshot Snapshot()
        => new(
            "The primary listing",
            "What it is about",
            string.Empty,
            [new ProgrammeGenre(7, 1)],
            Later,
            AudioMode.Undetermined,
            ProgrammeSnapshot.SoundsUnannounced);
}
