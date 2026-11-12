using Carina.Domain.Base;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

using static Carina.Domain.Tests.Reservations.ReservationFactory;

namespace Carina.Domain.Tests.Reservations;

public sealed class ReservationClaimTests
{
    [Fact(DisplayName = "BR-RS-001: a secured reservation is due from its effective start, as far ahead as the recorder prepares, until its effective end")]
    public void ASecuredReservationIsDueAcrossItsWindow()
    {
        Reservation secured = Planned(marginBefore: Margin.OfSeconds(30), marginAfter: Margin.OfSeconds(60));

        Assert.False(secured.IsDueAt(secured.EffectiveStartAt.AddTicks(-1), TimeSpan.Zero));
        Assert.True(secured.IsDueAt(secured.EffectiveStartAt.AddSeconds(-10), TimeSpan.FromSeconds(10)));
        Assert.True(secured.IsDueAt(secured.EffectiveStartAt, TimeSpan.Zero));
        Assert.True(secured.IsDueAt(secured.EffectiveEndAt.AddTicks(-1), TimeSpan.Zero));
        Assert.False(secured.IsDueAt(secured.EffectiveEndAt, TimeSpan.FromHours(1)));
    }

    [Theory(DisplayName = "BR-RS-001: a contended, cancelled or missed reservation does not start recording when its window opens")]
    [InlineData(ReservationState.Conflict)]
    [InlineData(ReservationState.Cancelled)]
    [InlineData(ReservationState.Missed)]
    public void AReservationNotSecuredDoesNotStartRecording(ReservationState state)
    {
        Reservation waiting = Rehydrated(state, null, null);

        Assert.False(waiting.IsDueAt(waiting.EffectiveStartAt, TimeSpan.Zero));
        Assert.False(waiting.IsReadyToBeClaimed);
    }

    [Fact(DisplayName = "BR-RS-002: a reservation being recorded stays due until its recording ends, whatever its state and window")]
    public void AReservationBeingRecordedStaysDueUntilItsRecordingEnds()
    {
        Reservation running = Rehydrated(ReservationState.Cancelled, Now, null);
        Reservation ended = Rehydrated(ReservationState.Scheduled, Now, RecordingOutcome.Complete);

        Assert.True(running.IsDueAt(running.EffectiveEndAt.AddDays(1), TimeSpan.Zero));
        Assert.False(ended.IsDueAt(ended.EffectiveStartAt, TimeSpan.Zero));
    }

    [Fact(DisplayName = "BR-RS-001: only a secured reservation nothing has claimed yet is claimed")]
    public void OnlyASecuredReservationNothingHasClaimedIsClaimed()
    {
        Assert.True(Planned().IsReadyToBeClaimed);
        Assert.False(Claimed().IsReadyToBeClaimed);
    }

    [Fact(DisplayName = "BR-RS-002: a claim is given back only by the start that made it, and never once a recording came of it")]
    public void AClaimIsGivenBackOnlyByTheStartThatMadeIt()
    {
        Reservation claimed = Claimed(Now);
        Reservation recorded = Rehydrated(ReservationState.Scheduled, Now, RecordingOutcome.Truncated);

        Assert.True(claimed.MayBeReleasedFrom(Now));
        Assert.False(claimed.MayBeReleasedFrom(Now.AddSeconds(1)));
        Assert.False(recorded.MayBeReleasedFrom(Now));
        Assert.False(Planned().MayBeReleasedFrom(Now));
    }

    [Fact(DisplayName = "BR-RS-001: a reservation is pending while it is secured or contended with no outcome, and claimed or meeting the window")]
    public void AReservationIsPendingWhileItWaitsAndMeetsTheWindow()
    {
        Reservation waiting = Planned();
        var meeting = new ReservationWindow(waiting.EndAt, waiting.EndAt.AddHours(1));
        var before = new ReservationWindow(waiting.StartAt.AddHours(-2), waiting.StartAt.AddTicks(-1));
        var after = new ReservationWindow(waiting.EndAt.AddTicks(1), waiting.EndAt.AddHours(1));

        Assert.True(waiting.IsPendingWithin(meeting));
        Assert.False(waiting.IsPendingWithin(before));
        Assert.False(waiting.IsPendingWithin(after));
        Assert.True(Rehydrated(ReservationState.Conflict, null, null).IsPendingWithin(meeting with { From = Now }));
        Assert.True(Claimed(Now).IsPendingWithin(after with { From = Now.AddYears(1), To = Now.AddYears(2) }));
        Assert.False(Rehydrated(ReservationState.Cancelled, null, null).IsPendingWithin(new ReservationWindow(Now, Now.AddDays(1))));
        Assert.False(Rehydrated(ReservationState.Scheduled, Now, RecordingOutcome.Complete)
            .IsPendingWithin(new ReservationWindow(Now, Now.AddDays(1))));
    }

    [Fact(DisplayName = "BR-RS-001: a reservation is thrown away only once nothing is left to record and no recording came of it")]
    public void AReservationIsThrownAwayOnlyOnceNothingIsLeftToRecord()
    {
        Reservation waiting = Planned();
        Reservation running = Claimed(Now);
        Reservation cancelled = Rehydrated(ReservationState.Cancelled, null, null);
        Reservation recorded = Rehydrated(ReservationState.Scheduled, Now, RecordingOutcome.Complete);

        Assert.Equal(ReservationDiscard.StillToBeRecorded, waiting.Discarding(false, Now));
        Assert.Equal(ReservationDiscard.Discarded, waiting.Discarding(false, waiting.EffectiveEndAt));
        Assert.Equal(ReservationDiscard.TurningIntoARecording, running.Discarding(false, running.EffectiveEndAt.AddDays(1)));
        Assert.Equal(ReservationDiscard.Discarded, cancelled.Discarding(false, Now));
        Assert.Equal(ReservationDiscard.RecordingCameOfIt, cancelled.Discarding(true, Now));
        Assert.Equal(ReservationDiscard.Discarded, recorded.Discarding(false, Now));
        Assert.Equal(ReservationDiscard.RecordingCameOfIt, recorded.Discarding(true, Now));
    }
}
