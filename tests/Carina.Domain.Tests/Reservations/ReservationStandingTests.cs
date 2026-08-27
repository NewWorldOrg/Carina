using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Reservations;

public sealed class ReservationStandingTests
{
    [Theory]
    [InlineData(ReservationState.Scheduled, ReservationStanding.Scheduled)]
    [InlineData(ReservationState.Conflict, ReservationStanding.Conflict)]
    [InlineData(ReservationState.Cancelled, ReservationStanding.Cancelled)]
    [InlineData(ReservationState.Missed, ReservationStanding.Missed)]
    public void AReservationNobodyHasClaimedStandsAsTheStateItOwns(
        ReservationState state,
        ReservationStanding standing)
    {
        Reservation reservation = ReservationFactory.Rehydrated(state, null, null);

        Assert.Equal(standing, reservation.Standing);
    }

    [Fact]
    public void AClaimedReservationWithNoOutcomeYetIsBeingRecorded()
    {
        Reservation reservation = ReservationFactory.Claimed();

        Assert.Equal(ReservationStanding.Recording, reservation.Standing);
        Assert.Equal(ReservationState.Scheduled, reservation.State);
    }

    [Theory]
    [InlineData(RecordingOutcome.Complete, ReservationStanding.Complete)]
    [InlineData(RecordingOutcome.Truncated, ReservationStanding.Truncated)]
    [InlineData(RecordingOutcome.Failed, ReservationStanding.Failed)]
    public void AnOutcomeRecordingWroteIsWhatTheReservationStandsAs(
        RecordingOutcome outcome,
        ReservationStanding standing)
    {
        Reservation reservation = ReservationFactory.Rehydrated(
            ReservationState.Scheduled,
            ReservationFactory.Now,
            outcome);

        Assert.Equal(standing, reservation.Standing);
    }

    [Fact]
    public void AnOutcomeIsReadBeforeTheClaimAndTheClaimBeforeTheStateItOwns()
    {
        Reservation ended = ReservationFactory.Rehydrated(
            ReservationState.Scheduled,
            ReservationFactory.Now,
            RecordingOutcome.Complete);
        Reservation claimed = ReservationFactory.Rehydrated(ReservationState.Scheduled, ReservationFactory.Now, null);

        Assert.Equal(ReservationStanding.Complete, ended.Standing);
        Assert.Equal(ReservationStanding.Recording, claimed.Standing);
    }

    [Fact]
    public void EveryStateThisDomainOwnsAndEveryOutcomeRecordingWritesHasAStandingOfItsOwn()
    {
        var named = new HashSet<ReservationStanding>();

        foreach (ReservationState state in Enum.GetValues<ReservationState>())
        {
            named.Add(ReservationFactory.Rehydrated(state, null, null).Standing);
        }

        foreach (RecordingOutcome outcome in Enum.GetValues<RecordingOutcome>())
        {
            named.Add(ReservationFactory
                .Rehydrated(ReservationState.Scheduled, ReservationFactory.Now, outcome)
                .Standing);
        }

        named.Add(ReservationFactory.Claimed().Standing);

        Assert.Equal([.. Enum.GetValues<ReservationStanding>()], [.. named.Order()]);
    }
}
