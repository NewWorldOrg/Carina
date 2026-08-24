using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;

namespace Carina.Domain.Tests.Reservations;

public sealed class ReservationTests
{
    [Fact]
    public void APlannedReservationIsSecuredAndOwesRecordingNothing()
    {
        Reservation reservation = ReservationFactory.Planned();

        Assert.Equal(ReservationState.Scheduled, reservation.State);
        Assert.Null(reservation.StartedAt);
        Assert.Null(reservation.RecordingOutcome);
        Assert.False(reservation.IsPinned);
        Assert.False(reservation.IsRuleBorn);
    }

    [Fact]
    public void AReservationBornOfARuleSaysSo()
    {
        Reservation reservation = ReservationFactory.Planned(ruleId: RuleId.New());

        Assert.True(reservation.IsRuleBorn);
    }

    [Fact]
    public void TheEffectiveWindowIsTheProgrammeWidenedByBothMargins()
    {
        Reservation reservation = ReservationFactory.Planned(
            marginBefore: Margin.OfSeconds(10),
            marginAfter: Margin.OfSeconds(30));

        Assert.Equal(reservation.StartAt.AddSeconds(-10), reservation.EffectiveStartAt);
        Assert.Equal(reservation.EndAt.AddSeconds(30), reservation.EffectiveEndAt);
    }

    [Fact]
    public void AScheduledReservationFallsToConflictAndBackAgain()
    {
        Reservation reservation = ReservationFactory.Planned();

        reservation.Contend();
        Assert.Equal(ReservationState.Conflict, reservation.State);

        reservation.Secure();
        Assert.Equal(ReservationState.Scheduled, reservation.State);
    }

    [Fact]
    public void AClaimedReservationKeepsTheCapacityItIsAlreadyUsing()
    {
        Reservation reservation = ReservationFactory.Claimed();

        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(reservation.Contend);

        Assert.Contains("already using", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(ReservationState.Scheduled, reservation.State);
    }

    [Fact]
    public void AClaimedReservationIsNotMissed()
    {
        Reservation reservation = ReservationFactory.Claimed();

        Assert.Throws<InvalidOperationException>(reservation.Miss);
    }

    [Fact]
    public void ACancelledReservationIsKeptAndCanComeBack()
    {
        Reservation reservation = ReservationFactory.Planned();

        reservation.Cancel();
        Assert.Equal(ReservationState.Cancelled, reservation.State);

        reservation.Restore();
        Assert.Equal(ReservationState.Scheduled, reservation.State);
    }

    [Fact]
    public void ACancelledReservationDoesNotFallToConflict()
    {
        Reservation reservation = ReservationFactory.Planned();
        reservation.Cancel();

        Assert.Throws<InvalidOperationException>(reservation.Contend);
        Assert.Throws<InvalidOperationException>(reservation.Miss);
    }

    [Fact]
    public void AMissedReservationIsTheEndOfTheLine()
    {
        Reservation reservation = ReservationFactory.Planned();
        reservation.Miss();

        Assert.Equal(ReservationState.Missed, reservation.State);
        Assert.Throws<InvalidOperationException>(reservation.Secure);
        Assert.Throws<InvalidOperationException>(reservation.Restore);
    }

    [Fact]
    public void AReservationOffersNoWayToWriteWhatRecordingOwns()
    {
        foreach (string name in new[] { nameof(Reservation.StartedAt), nameof(Reservation.RecordingOutcome) })
        {
            Assert.False(typeof(Reservation).GetProperty(name)!.SetMethod!.IsPublic);
        }
    }

    [Fact]
    public void AnOutcomeWithoutAClaimIsRefused()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(
            () => ReservationFactory.Rehydrated(ReservationState.Scheduled, null, RecordingOutcome.Complete));

        Assert.Equal("recordingOutcome", refusal.ParamName);
    }

    [Fact]
    public void ADivergedReservationSaysWhatDiverged()
    {
        Assert.Throws<ArgumentException>(
            () => ReservationFactory.Rehydrated(ReservationState.Scheduled, null, null, epgDiverged: true));

        Assert.Throws<ArgumentException>(
            () => ReservationFactory.Rehydrated(
                ReservationState.Scheduled,
                null,
                null,
                divergences: [new EpgDivergence(DivergedField.StartAt, "a", "b", ReservationFactory.Now)]));
    }

    [Fact]
    public void OnlyADivergenceOrADisappearanceIsAcknowledged()
    {
        Assert.Throws<ArgumentException>(
            () => ReservationFactory.Rehydrated(
                ReservationState.Scheduled,
                null,
                null,
                acknowledgedAt: ReservationFactory.Now));

        Reservation reservation = ReservationFactory.Planned();

        Assert.Throws<InvalidOperationException>(() => reservation.Acknowledge(ReservationFactory.Now));
    }

    [Fact]
    public void AcknowledgingClearsWhenTheDivergenceComesBack()
    {
        Reservation reservation = ReservationFactory.Planned();

        reservation.Diverge([new EpgDivergence(DivergedField.Name, "before", "after", ReservationFactory.Now)]);
        reservation.Acknowledge(ReservationFactory.Now);
        Assert.Equal(ReservationFactory.Now, reservation.AcknowledgedAt);

        reservation.Diverge([new EpgDivergence(DivergedField.EndAt, "before", "after", ReservationFactory.Now)]);
        Assert.Null(reservation.AcknowledgedAt);
    }

    [Fact]
    public void AReservationInAGroupRoleNamesTheBroadcastItBelongsTo()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(
            () => ReservationFactory.Rehydrated(
                ReservationState.Scheduled,
                null,
                null,
                groupRole: BroadcastGroupRole.RelaySegment));

        Assert.Equal("broadcastGroupKey", refusal.ParamName);
    }

    [Fact]
    public void RegroupingCarriesTheReservationOverRatherThanReplacingIt()
    {
        Reservation reservation = ReservationFactory.Planned();
        ReservationId id = reservation.Id;

        reservation.Regroup(new BroadcastGroupKey("moved-4001"), BroadcastGroupRole.MovementPrimary);

        Assert.Equal(id, reservation.Id);
        Assert.Equal(BroadcastGroupRole.MovementPrimary, reservation.BroadcastGroupRole);
        Assert.Throws<ArgumentException>(() => reservation.Regroup(null, BroadcastGroupRole.MovementSuppressed));
    }

    [Fact]
    public void AReservationEndsAfterItStarts()
    {
        Reservation reservation = ReservationFactory.Planned();

        Assert.Throws<ArgumentException>(
            () => reservation.Reframe(reservation.StartAt, reservation.StartAt, true));
    }

    [Fact]
    public void TheFourStatesAreTheOnlyOnesThisDomainOwns()
    {
        Assert.Equal(
            [ReservationState.Scheduled, ReservationState.Conflict, ReservationState.Cancelled, ReservationState.Missed],
            Enum.GetValues<ReservationState>());
    }
}
