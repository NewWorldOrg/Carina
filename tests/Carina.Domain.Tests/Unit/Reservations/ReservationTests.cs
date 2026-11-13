using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;

namespace Carina.Domain.Tests.Unit.Reservations;

[Trait("Category", "Unit")]
public sealed class ReservationTests
{
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(10);

    private static readonly DateTime Later = ReservationFactory.Now.AddMinutes(5);

    private static readonly DateTime Airs = new(2026, 8, 24, 20, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Ends = Airs.AddMinutes(200);

    private static readonly Margin Ahead = Margin.OfSeconds(120);

    private static readonly Margin Behind = Margin.OfSeconds(180);

    private static readonly BroadcastGroupKey Key = new("movement:32736-1024-4001");

    private static readonly DateTime Noon = new(2026, 9, 17, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Now = ReservationFactory.Now;

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

        reservation.Cancel(ReservationCancellation.ByHand);
        Assert.Equal(ReservationState.Cancelled, reservation.State);

        reservation.Restore();
        Assert.Equal(ReservationState.Scheduled, reservation.State);
    }

    [Theory]
    [InlineData(ReservationCancellation.ByHand)]
    [InlineData(ReservationCancellation.ProgrammeGone)]
    public void ACancelledReservationCarriesWhyItWasCancelledUntilItComesBack(ReservationCancellation why)
    {
        Reservation reservation = ReservationFactory.Planned();

        reservation.Cancel(why);
        Assert.Equal(why, reservation.Cancellation);

        reservation.Restore();
        Assert.Null(reservation.Cancellation);
    }

    [Fact]
    public void AReasonThisDomainDoesNotOwnIsNotACancellation()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => ReservationFactory.Planned().Cancel((ReservationCancellation)9));

    [Fact]
    public void ACancelledReservationReadBackWithoutAReasonIsRefused()
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(() => Reservation.Rehydrate(
            ReservationId.New(),
            ReservationFactory.Programme(),
            null,
            Priority.Default,
            ReservationFactory.Now.AddHours(2),
            ReservationFactory.Now.AddHours(3),
            true,
            Margin.None,
            Margin.None,
            ReservationFactory.Snapshot(),
            null,
            BroadcastGroupRole.Standalone,
            ReservationState.Cancelled,
            null,
            null,
            false,
            [],
            false,
            null,
            false,
            null,
            ReservationFactory.Now));

        Assert.Equal("cancellation", refused.ParamName);
    }

    [Fact]
    public void AReservationStillInTheRunningReadBackWithAReasonIsRefusedToo()
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(() => ReservationFactory.Rehydrated(
            ReservationState.Scheduled,
            null,
            null,
            cancellation: ReservationCancellation.ByHand));

        Assert.Equal("cancellation", refused.ParamName);
    }

    [Fact]
    public void ACancelledReservationDoesNotFallToConflict()
    {
        Reservation reservation = ReservationFactory.Planned();
        reservation.Cancel(ReservationCancellation.ByHand);

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
    public void AClaimThatCameToNothingIsLetGoOfAndTheReservationSettles()
    {
        Reservation reservation = ReservationFactory.Claimed();

        Assert.True(reservation.IsPinned);
        Assert.Throws<InvalidOperationException>(reservation.Miss);

        reservation.Abandon();

        Assert.False(reservation.IsPinned);
        Assert.Null(reservation.StartedAt);
        Assert.Equal(ReservationState.Missed, reservation.State);
        Assert.Equal(ReservationStanding.Missed, reservation.Standing);
    }

    [Fact]
    public void AClaimARecordingCameOfIsNotAbandoned()
    {
        Reservation reservation = ReservationFactory.Rehydrated(
            ReservationState.Scheduled,
            ReservationFactory.Now,
            RecordingOutcome.Complete);

        Assert.Throws<InvalidOperationException>(reservation.Abandon);
        Assert.True(reservation.IsPinned);
        Assert.Equal(ReservationStanding.Complete, reservation.Standing);
    }

    [Fact]
    public void AnAbandonedReservationIsTheEndOfTheLineToo()
    {
        Reservation reservation = ReservationFactory.Claimed();
        reservation.Abandon();

        Assert.Throws<InvalidOperationException>(reservation.Abandon);
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
    public void APlannedReservationHasSomewhereToTune()
    {
        Reservation reservation = ReservationFactory.Planned();

        Assert.False(reservation.ReceptionUnavailable);
        Assert.Null(reservation.ReceptionUnavailableSince);
    }

    [Fact]
    public void LosingReceptionIsAMarkAndNotAStateOfItsOwn()
    {
        Reservation reservation = ReservationFactory.Planned();

        reservation.LoseReception(ReservationFactory.Now);

        Assert.True(reservation.ReceptionUnavailable);
        Assert.Equal(ReservationFactory.Now, reservation.ReceptionUnavailableSince);
        Assert.Equal(ReservationState.Scheduled, reservation.State);
    }

    [Fact]
    public void GainingReceptionBackTakesTheMarkOffAgain()
    {
        Reservation reservation = ReservationFactory.Planned();
        reservation.LoseReception(ReservationFactory.Now);

        reservation.RegainReception();

        Assert.False(reservation.ReceptionUnavailable);
        Assert.Null(reservation.ReceptionUnavailableSince);
        Assert.Equal(ReservationState.Scheduled, reservation.State);
    }

    [Fact]
    public void TheMomentReceptionWasLostIsKeptInUtc()
    {
        Reservation reservation = ReservationFactory.Planned();

        ArgumentException refused = Assert.Throws<ArgumentException>(
            () => reservation.LoseReception(DateTime.SpecifyKind(ReservationFactory.Now, DateTimeKind.Local)));

        Assert.Equal("at", refused.ParamName);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void AMarkAndTheMomentItWasNoticedComeTogether(bool unavailable, bool noticed)
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(() => ReservationFactory.Rehydrated(
            ReservationState.Scheduled,
            null,
            null,
            receptionUnavailable: unavailable,
            receptionUnavailableSince: noticed ? ReservationFactory.Now : null));

        Assert.Equal("receptionUnavailableSince", refused.ParamName);
    }

    [Fact]
    public void AMarkSurvivesBeingRehydrated()
    {
        Reservation reservation = ReservationFactory.Rehydrated(
            ReservationState.Scheduled,
            null,
            null,
            receptionUnavailable: true,
            receptionUnavailableSince: ReservationFactory.Now);

        Assert.True(reservation.ReceptionUnavailable);
        Assert.Equal(ReservationFactory.Now, reservation.ReceptionUnavailableSince);
    }

    [Fact]
    public void AReservationWithNowhereToTuneIsNotSecured()
    {
        Reservation reservation = ReservationFactory.Planned();
        reservation.LoseReception(ReservationFactory.Now);

        Assert.Throws<InvalidOperationException>(reservation.Secure);
        Assert.Equal(ReservationState.Scheduled, reservation.State);
    }

    [Fact]
    public void SomewhereToTuneAgainIsWhatLetsAReservationBeSecured()
    {
        Reservation reservation = ReservationFactory.Rehydrated(ReservationState.Conflict, null, null);
        reservation.LoseReception(ReservationFactory.Now);

        reservation.RegainReception();
        reservation.Secure();

        Assert.Equal(ReservationState.Scheduled, reservation.State);
    }

    [Fact]
    public void ACancelledReservationIsRefusedForBeingCancelledRatherThanForItsReception()
    {
        Reservation reservation = ReservationFactory.Planned();
        reservation.LoseReception(ReservationFactory.Now);
        reservation.Cancel(ReservationCancellation.ByHand);

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(reservation.Secure);

        Assert.Contains(nameof(ReservationState.Cancelled), refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMomentReceptionWasLostIsReadBackInUtc()
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(() => ReservationFactory.Rehydrated(
            ReservationState.Scheduled,
            null,
            null,
            receptionUnavailable: true,
            receptionUnavailableSince: DateTime.SpecifyKind(ReservationFactory.Now, DateTimeKind.Local)));

        Assert.Equal("receptionUnavailableSince", refused.ParamName);
    }

    [Fact]
    public void AClaimedReservationIsNotCancelledOutFromUnderTheRecording()
    {
        Reservation reservation = ReservationFactory.Claimed();

        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() => reservation.Cancel(ReservationCancellation.ByHand));

        Assert.Contains("holding a tuner", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(ReservationState.Scheduled, reservation.State);
    }

    [Fact]
    public void AReservationNobodyHasClaimedIsCancelled()
    {
        Reservation reservation = ReservationFactory.Planned();

        reservation.Cancel(ReservationCancellation.ByHand);

        Assert.Equal(ReservationState.Cancelled, reservation.State);
    }

    [Fact]
    public void BothMarginsMoveTheEffectiveWindowWhenTheyAreChanged()
    {
        Reservation reservation = ReservationFactory.Planned(
            marginBefore: Margin.OfSeconds(10),
            marginAfter: Margin.OfSeconds(10));

        reservation.Remargin(Margin.OfSeconds(60), Margin.OfSeconds(180));

        Assert.Equal(60, reservation.MarginBefore.Seconds);
        Assert.Equal(180, reservation.MarginAfter.Seconds);
        Assert.Equal(reservation.StartAt.AddSeconds(-60), reservation.EffectiveStartAt);
        Assert.Equal(reservation.EndAt.AddSeconds(180), reservation.EffectiveEndAt);
    }

    [Fact]
    public void ANewPriorityIsWhatTheReservationCarriesAfterwards()
    {
        Reservation reservation = ReservationFactory.Planned(priority: new Priority(10));

        reservation.Reprioritise(new Priority(40));

        Assert.Equal(40, reservation.Priority.Value);
    }

    [Fact]
    public void TheFourStatesAreTheOnlyOnesThisDomainOwns()
    {
        Assert.Equal(
            [ReservationState.Scheduled, ReservationState.Conflict, ReservationState.Cancelled, ReservationState.Missed],
            Enum.GetValues<ReservationState>());
    }

    [Fact(DisplayName = "BR-RS-001: a secured reservation is due from its effective start, as far ahead as the recorder prepares, until its effective end")]
    public void ASecuredReservationIsDueAcrossItsWindow()
    {
        Reservation secured = ReservationFactory.Planned(marginBefore: Margin.OfSeconds(30), marginAfter: Margin.OfSeconds(60));

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
        Reservation waiting = ReservationFactory.Rehydrated(state, null, null);

        Assert.False(waiting.IsDueAt(waiting.EffectiveStartAt, TimeSpan.Zero));
        Assert.False(waiting.IsReadyToBeClaimed);
    }

    [Fact(DisplayName = "BR-RS-002: a reservation being recorded stays due until its recording ends, whatever its state and window")]
    public void AReservationBeingRecordedStaysDueUntilItsRecordingEnds()
    {
        Reservation running = ReservationFactory.Rehydrated(ReservationState.Cancelled, ReservationFactory.Now, null);
        Reservation ended = ReservationFactory.Rehydrated(ReservationState.Scheduled, ReservationFactory.Now, RecordingOutcome.Complete);

        Assert.True(running.IsDueAt(running.EffectiveEndAt.AddDays(1), TimeSpan.Zero));
        Assert.False(ended.IsDueAt(ended.EffectiveStartAt, TimeSpan.Zero));
    }

    [Fact(DisplayName = "BR-RS-001: only a secured reservation nothing has claimed yet is claimed")]
    public void OnlyASecuredReservationNothingHasClaimedIsClaimed()
    {
        Assert.True(ReservationFactory.Planned().IsReadyToBeClaimed);
        Assert.False(ReservationFactory.Claimed().IsReadyToBeClaimed);
    }

    [Fact(DisplayName = "BR-RS-002: a claim is given back only by the start that made it, and never once a recording came of it")]
    public void AClaimIsGivenBackOnlyByTheStartThatMadeIt()
    {
        Reservation claimed = ReservationFactory.Claimed(ReservationFactory.Now);
        Reservation recorded = ReservationFactory.Rehydrated(ReservationState.Scheduled, ReservationFactory.Now, RecordingOutcome.Truncated);

        Assert.True(claimed.MayBeReleasedFrom(ReservationFactory.Now));
        Assert.False(claimed.MayBeReleasedFrom(ReservationFactory.Now.AddSeconds(1)));
        Assert.False(recorded.MayBeReleasedFrom(ReservationFactory.Now));
        Assert.False(ReservationFactory.Planned().MayBeReleasedFrom(ReservationFactory.Now));
    }

    [Fact(DisplayName = "BR-RS-001: a reservation is pending while it is secured or contended with no outcome, and claimed or meeting the window")]
    public void AReservationIsPendingWhileItWaitsAndMeetsTheWindow()
    {
        Reservation waiting = ReservationFactory.Planned();
        var meeting = new ReservationWindow(waiting.EndAt, waiting.EndAt.AddHours(1));
        var before = new ReservationWindow(waiting.StartAt.AddHours(-2), waiting.StartAt.AddTicks(-1));
        var after = new ReservationWindow(waiting.EndAt.AddTicks(1), waiting.EndAt.AddHours(1));

        Assert.True(waiting.IsPendingWithin(meeting));
        Assert.False(waiting.IsPendingWithin(before));
        Assert.False(waiting.IsPendingWithin(after));
        Assert.True(ReservationFactory.Rehydrated(ReservationState.Conflict, null, null).IsPendingWithin(meeting with { From = ReservationFactory.Now }));
        Assert.True(ReservationFactory.Claimed(ReservationFactory.Now).IsPendingWithin(after with { From = ReservationFactory.Now.AddYears(1), To = ReservationFactory.Now.AddYears(2) }));
        Assert.False(ReservationFactory.Rehydrated(ReservationState.Cancelled, null, null).IsPendingWithin(new ReservationWindow(ReservationFactory.Now, ReservationFactory.Now.AddDays(1))));
        Assert.False(ReservationFactory.Rehydrated(ReservationState.Scheduled, ReservationFactory.Now, RecordingOutcome.Complete)
            .IsPendingWithin(new ReservationWindow(ReservationFactory.Now, ReservationFactory.Now.AddDays(1))));
    }

    [Fact(DisplayName = "BR-RS-001: a reservation is thrown away only once nothing is left to record and no recording came of it")]
    public void AReservationIsThrownAwayOnlyOnceNothingIsLeftToRecord()
    {
        Reservation waiting = ReservationFactory.Planned();
        Reservation running = ReservationFactory.Claimed(ReservationFactory.Now);
        Reservation cancelled = ReservationFactory.Rehydrated(ReservationState.Cancelled, null, null);
        Reservation recorded = ReservationFactory.Rehydrated(ReservationState.Scheduled, ReservationFactory.Now, RecordingOutcome.Complete);

        Assert.Equal(ReservationDiscard.StillToBeRecorded, waiting.Discarding(false, ReservationFactory.Now));
        Assert.Equal(ReservationDiscard.Discarded, waiting.Discarding(false, waiting.EffectiveEndAt));
        Assert.Equal(ReservationDiscard.TurningIntoARecording, running.Discarding(false, running.EffectiveEndAt.AddDays(1)));
        Assert.Equal(ReservationDiscard.Discarded, cancelled.Discarding(false, ReservationFactory.Now));
        Assert.Equal(ReservationDiscard.RecordingCameOfIt, cancelled.Discarding(true, ReservationFactory.Now));
        Assert.Equal(ReservationDiscard.Discarded, recorded.Discarding(false, ReservationFactory.Now));
        Assert.Equal(ReservationDiscard.RecordingCameOfIt, recorded.Discarding(true, ReservationFactory.Now));
    }

    [Fact(DisplayName = "BR-RS-001: a reservation being recorded is not revised, whatever the move")]
    public void AReservationBeingRecordedIsNotRevised()
    {
        Reservation claimed = ReservationFactory.Claimed();

        Assert.Equal(ReservationRevisionRefusal.AlreadyRecording, claimed.RefusalToRevise(ReservationMove.Keep, ReservationFactory.Now));
        Assert.Equal(ReservationRevisionRefusal.AlreadyRecording, claimed.RefusalToRevise(ReservationMove.Cancel, ReservationFactory.Now));
        Assert.Equal(ReservationRevisionRefusal.AlreadyRecording, claimed.RefusalToRevise(ReservationMove.Restore, ReservationFactory.Now));
    }

    [Theory(DisplayName = "BR-RS-001: only a reservation still waiting for its tuner is changed or cancelled")]
    [InlineData(ReservationState.Scheduled, null)]
    [InlineData(ReservationState.Conflict, null)]
    [InlineData(ReservationState.Cancelled, ReservationRevisionRefusal.NotStanding)]
    [InlineData(ReservationState.Missed, ReservationRevisionRefusal.NotStanding)]
    public void OnlyAReservationStillWaitingIsChanged(ReservationState state, ReservationRevisionRefusal? refused)
    {
        Reservation reservation = ReservationFactory.Rehydrated(state, null, null);

        Assert.Equal(refused, reservation.RefusalToRevise(ReservationMove.Keep, ReservationFactory.Now));
        Assert.Equal(refused, reservation.RefusalToRevise(ReservationMove.Cancel, ReservationFactory.Now));
    }

    [Theory(DisplayName = "BR-RS-001: only a cancelled reservation is restored")]
    [InlineData(ReservationState.Scheduled)]
    [InlineData(ReservationState.Conflict)]
    [InlineData(ReservationState.Missed)]
    public void OnlyACancelledReservationIsRestored(ReservationState state)
        => Assert.Equal(
            ReservationRevisionRefusal.NotCancelled,
            ReservationFactory.Rehydrated(state, null, null).RefusalToRevise(ReservationMove.Restore, ReservationFactory.Now));

    [Fact(DisplayName = "BR-RS-001: a cancelled reservation is restored until its window closes, and not from then on")]
    public void ACancelledReservationIsRestoredUntilItsWindowCloses()
    {
        Reservation cancelled = ReservationFactory.Rehydrated(ReservationState.Cancelled, null, null);

        Assert.Null(cancelled.RefusalToRevise(ReservationMove.Restore, cancelled.EffectiveEndAt.AddTicks(-1)));
        Assert.Equal(
            ReservationRevisionRefusal.AlreadyOver,
            cancelled.RefusalToRevise(ReservationMove.Restore, cancelled.EffectiveEndAt));
    }

    [Fact(DisplayName = "BR-RD-010: a reservation is yet to begin while it holds no tuner and its window has not opened")]
    public void AReservationIsYetToBeginBeforeItsWindowOpens()
    {
        Reservation waiting = ReservationFactory.Planned(marginBefore: Margin.OfSeconds(60));

        Assert.True(waiting.IsYetToBeginAt(waiting.EffectiveStartAt.AddTicks(-1)));
        Assert.False(waiting.IsYetToBeginAt(waiting.EffectiveStartAt));
        Assert.False(ReservationFactory.Claimed(ReservationFactory.Now.AddHours(-1)).IsYetToBeginAt(ReservationFactory.Now.AddHours(-2)));
    }

    [Fact(DisplayName = "BR-RD-010: a reservation already holding the key and the role it is placed in is not regrouped")]
    public void AReservationAlreadyGroupedSoIsNotRegrouped()
    {
        var key = new BroadcastGroupKey("relay:32736-1024-4001");
        Reservation grouped = ReservationFactory.Rehydrated(ReservationState.Scheduled, null, null, key, BroadcastGroupRole.RelaySegment);

        Assert.True(grouped.IsGroupedAs(new BroadcastGroupKey("relay:32736-1024-4001"), BroadcastGroupRole.RelaySegment));
        Assert.False(grouped.IsGroupedAs(key, BroadcastGroupRole.MovementSuppressed));
        Assert.False(grouped.IsGroupedAs(null, BroadcastGroupRole.Standalone));
        Assert.True(ReservationFactory.Planned().IsGroupedAs(null, BroadcastGroupRole.Standalone));
    }

    [Fact(DisplayName = "BR-RD-010: a segment reserved beside another keeps that reservation's rule, priority, margins and wish")]
    public void ASegmentReservedBesideAnotherFollowsItsExample()
    {
        var rule = RuleId.New();
        Reservation example = Reservation.Plan(
            ReservationId.New(),
            ReservationFactory.Programme(),
            rule,
            new Priority(7),
            ReservationFactory.Programme().StartsAt,
            ReservationFactory.Programme().StartsAt.AddHours(1),
            true,
            Margin.OfSeconds(30),
            Margin.OfSeconds(90),
            ReservationFactory.Snapshot(),
            new BroadcastGroupKey("relay:32736-1024-4001"),
            BroadcastGroupRole.RelaySegment,
            ReservationFactory.Now,
            encodeWhenRecorded: false);
        Programme later = Listing(4002, ReservationFactory.Now.AddHours(4), ReservationFactory.Now.AddHours(5));
        var sibling = new BroadcastTarget(later, new BroadcastGroupKey("relay:32736-1024-4001"), BroadcastGroupRole.RelaySegment);
        var id = ReservationId.New();

        Reservation beside = example.PlanBeside(id, sibling, ReservationFactory.Now);

        Assert.Equal(id, beside.Id);
        Assert.Equal(sibling.Reference, beside.Programme);
        Assert.Equal(rule, beside.RuleId);
        Assert.Equal(new Priority(7), beside.Priority);
        Assert.Equal(Margin.OfSeconds(30), beside.MarginBefore);
        Assert.Equal(Margin.OfSeconds(90), beside.MarginAfter);
        Assert.False(beside.EncodeWhenRecorded);
        Assert.Equal(later.EndsAt, beside.EndAt);
        Assert.True(beside.EndAtConfirmed);
        Assert.Equal(later.Name, beside.SnapshotName);
        Assert.Equal(sibling.Key, beside.BroadcastGroupKey);
        Assert.Equal(BroadcastGroupRole.RelaySegment, beside.BroadcastGroupRole);
        Assert.Equal(ReservationState.Scheduled, beside.State);
    }

    [Fact(DisplayName = "BR-RD-010: a segment beside another whose end is not announced runs for the provisional length")]
    public void ASegmentWhoseEndIsNotAnnouncedRunsForTheProvisionalLength()
    {
        Programme later = Listing(4002, ReservationFactory.Now.AddHours(4), null);

        Reservation beside = ReservationFactory.Planned().PlanBeside(
            ReservationId.New(),
            new BroadcastTarget(later, new BroadcastGroupKey("relay:32736-1024-4001"), BroadcastGroupRole.RelaySegment),
            ReservationFactory.Now);

        Assert.Equal(later.StartsAt + Reservation.ProvisionalLengthWhenTheEndIsNotAnnounced, beside.EndAt);
        Assert.False(beside.EndAtConfirmed);
    }

    [Fact(DisplayName = "BR-RD-010: a reservation is as its rule asks while its priority, margins and wish are the rule's")]
    public void AReservationIsAsItsRuleAsks()
    {
        Rule rule = Rule.Draft(
            RuleId.New(),
            "A rule",
            new RuleQuery("keyword=news"),
            new Priority(3),
            true,
            Margin.OfSeconds(10),
            Margin.OfSeconds(20),
            ReservationFactory.Now);
        Reservation made = Reservation.Plan(
            ReservationId.New(),
            ReservationFactory.Programme(),
            rule.Id,
            new Priority(3),
            ReservationFactory.Programme().StartsAt,
            ReservationFactory.Programme().StartsAt.AddHours(1),
            true,
            Margin.OfSeconds(10),
            Margin.OfSeconds(20),
            ReservationFactory.Snapshot(),
            null,
            BroadcastGroupRole.Standalone,
            ReservationFactory.Now);

        Assert.True(made.IsAsTheRuleAsks(rule));

        made.Rewish(false);
        Assert.False(made.IsAsTheRuleAsks(rule));

        made.Rewish(true);
        made.Reprioritise(new Priority(4));
        Assert.False(made.IsAsTheRuleAsks(rule));

        made.Reprioritise(new Priority(3));
        made.Remargin(Margin.OfSeconds(10), Margin.OfSeconds(21));
        Assert.False(made.IsAsTheRuleAsks(rule));
    }

    [Fact(DisplayName = "BR-RS-001: a rule brings back only its own reservation the guide took out, while its window is open")]
    public void ARuleBringsBackOnlyItsOwnReservationTheGuideTookOut()
    {
        var rule = RuleId.New();
        Reservation gone = ReservationFactory.Rehydrated(
            ReservationState.Cancelled,
            null,
            null,
            cancellation: ReservationCancellation.ProgrammeGone,
            ruleId: rule);

        Assert.True(gone.MayBeRevivedByRule(gone.EffectiveEndAt.AddTicks(-1)));
        Assert.False(gone.MayBeRevivedByRule(gone.EffectiveEndAt));
        Assert.False(ReservationFactory.Rehydrated(ReservationState.Cancelled, null, null, cancellation: ReservationCancellation.ProgrammeGone)
            .MayBeRevivedByRule(ReservationFactory.Now));
        Assert.False(ReservationFactory.Rehydrated(ReservationState.Cancelled, null, null, cancellation: ReservationCancellation.ByHand, ruleId: rule)
            .MayBeRevivedByRule(ReservationFactory.Now));
        Assert.False(ReservationFactory.Rehydrated(ReservationState.Scheduled, null, null, ruleId: rule).MayBeRevivedByRule(ReservationFactory.Now));
    }

    [Theory(DisplayName = "BR-RS-003: a rule withdraws only a reservation it made that is secured or contended and not yet recording")]
    [InlineData(ReservationState.Scheduled, true)]
    [InlineData(ReservationState.Conflict, true)]
    [InlineData(ReservationState.Cancelled, false)]
    [InlineData(ReservationState.Missed, false)]
    public void ARuleWithdrawsOnlyWhatItMadeAndStillWaits(ReservationState state, bool withdrawn)
    {
        Reservation made = ReservationFactory.Rehydrated(state, null, null, ruleId: RuleId.New());

        Assert.Equal(withdrawn, made.MayBeWithdrawnByRule(RuleWithdrawal.WhenTheRuleIsDeleted, Grace, ReservationFactory.Now));
        Assert.Equal(withdrawn, made.MayBeWithdrawnByRule(RuleWithdrawal.WhenTheRuleIsSwitchedOff, Grace, ReservationFactory.Now));
    }

    [Fact(DisplayName = "BR-RS-003: a rule withdraws neither a reservation made by hand nor one being recorded")]
    public void ARuleWithdrawsNeitherAHandMadeNorARecordingReservation()
    {
        Reservation recording = ReservationFactory.Rehydrated(ReservationState.Scheduled, ReservationFactory.Now, null, ruleId: RuleId.New());

        Assert.False(ReservationFactory.Planned().MayBeWithdrawnByRule(RuleWithdrawal.WhenTheRuleIsDeleted, Grace, ReservationFactory.Now));
        Assert.False(recording.MayBeWithdrawnByRule(RuleWithdrawal.WhenTheRuleIsDeleted, Grace, ReservationFactory.Now));
    }

    [Fact(DisplayName = "BR-RS-003: a rule that still stands withdraws only what starts more than the grace away, and one switched off or deleted ignores the grace")]
    public void ARuleThatStillStandsLeavesWhatStartsWithinTheGrace()
    {
        Reservation made = ReservationFactory.Planned(ruleId: RuleId.New());
        DateTime atTheGrace = made.EffectiveStartAt - Grace;

        Assert.True(made.MayBeWithdrawnByRule(RuleWithdrawal.WhileTheRuleStands, Grace, atTheGrace.AddTicks(-1)));
        Assert.False(made.MayBeWithdrawnByRule(RuleWithdrawal.WhileTheRuleStands, Grace, atTheGrace));
        Assert.True(made.MayBeWithdrawnByRule(RuleWithdrawal.WhenTheRuleIsSwitchedOff, Grace, atTheGrace));
        Assert.True(made.MayBeWithdrawnByRule(RuleWithdrawal.WhenTheRuleIsDeleted, Grace, atTheGrace));
    }

    [Fact(DisplayName = "BR-RD-006: a programme is still to come until its end, or until the provisional length when no end is announced")]
    public void AProgrammeIsStillToComeUntilItsEnd()
    {
        Programme announced = Listing(4001, ReservationFactory.Now, ReservationFactory.Now.AddMinutes(30));
        Programme open = Listing(4002, ReservationFactory.Now, null);

        Assert.True(announced.IsStillToComeAt(ReservationFactory.Now.AddMinutes(30).AddTicks(-1)));
        Assert.False(announced.IsStillToComeAt(ReservationFactory.Now.AddMinutes(30)));
        Assert.True(open.IsStillToComeAt(ReservationFactory.Now + Reservation.ProvisionalLengthWhenTheEndIsNotAnnounced - TimeSpan.FromTicks(1)));
        Assert.False(open.IsStillToComeAt(ReservationFactory.Now + Reservation.ProvisionalLengthWhenTheEndIsNotAnnounced));
    }

    [Fact]
    public void AReservationMovesOntoTheHourTheBroadcastIsAnnouncedAtNow()
    {
        Reservation booked = ReservationFactory.Planned();
        DateTime moved = booked.ProgrammeStartsAt.AddMinutes(30);

        booked.Follow(moved, moved.AddHours(1), true, SnapshotFromTheGuideLater(), [Slipped(booked)]);

        Assert.Equal(moved, booked.StartAt);
        Assert.Equal(moved.AddHours(1), booked.EndAt);
        Assert.Equal(moved, booked.ProgrammeStartsAt);
        Assert.Equal(moved, booked.Programme.StartsAt);
    }

    [Fact]
    public void TheBroadcastKeepsTheIdentityItWasReservedUnderWhereverItMovesTo()
    {
        Reservation booked = ReservationFactory.Planned();
        ProgrammeRef before = booked.Programme;
        DateTime moved = booked.ProgrammeStartsAt.AddHours(3);

        booked.Follow(moved, moved.AddHours(1), true, SnapshotFromTheGuideLater("その後の題名"), [Slipped(booked)]);

        Assert.Equal(before.NetworkId, booked.NetworkId);
        Assert.Equal(before.ServiceId, booked.ServiceId);
        Assert.Equal(before.EventId, booked.EventId);
        Assert.Equal("その後の題名", booked.SnapshotName);
    }

    [Fact]
    public void FollowingSaysWhatMovedAndPutsTheAcknowledgementBack()
    {
        Reservation booked = ReservationFactory.Planned();
        DateTime moved = booked.ProgrammeStartsAt.AddMinutes(30);
        EpgDivergence said = Slipped(booked);

        booked.Follow(moved, moved.AddHours(1), true, SnapshotFromTheGuideLater(), [said]);
        booked.Acknowledge(Later);
        booked.Follow(moved.AddMinutes(30), moved.AddMinutes(90), true, SnapshotFromTheGuideLater(), [said]);

        Assert.True(booked.EpgDiverged);
        Assert.Equal([said], booked.EpgDivergences);
        Assert.Null(booked.AcknowledgedAt);
    }

    [Fact]
    public void AReservationDoesNotMoveWithoutSayingWhatMoved()
    {
        Reservation booked = ReservationFactory.Planned();

        Assert.Throws<ArgumentException>(() => booked.Follow(
            booked.ProgrammeStartsAt.AddMinutes(30),
            booked.ProgrammeStartsAt.AddMinutes(90),
            true,
            SnapshotFromTheGuideLater(),
            []));
    }

    [Fact]
    public void AReservationAlreadyHoldingATunerIsNotMovedUnderneathTheRecording()
    {
        Reservation recording = ReservationFactory.Claimed();

        Assert.Throws<InvalidOperationException>(() => recording.Follow(
            recording.ProgrammeStartsAt.AddMinutes(30),
            recording.ProgrammeStartsAt.AddMinutes(90),
            true,
            SnapshotFromTheGuideLater(),
            [Slipped(recording)]));
    }

    [Fact]
    public void AReservationSomebodyCancelledIsNotMovedBackIntoTheRunning()
    {
        Reservation cancelled = ReservationFactory.Rehydrated(ReservationState.Cancelled, null, null);

        Assert.Throws<InvalidOperationException>(() => cancelled.Follow(
            cancelled.ProgrammeStartsAt.AddMinutes(30),
            cancelled.ProgrammeStartsAt.AddMinutes(90),
            true,
            SnapshotFromTheGuideLater(),
            [Slipped(cancelled)]));
    }

    [Fact]
    public void ABroadcastThatVanishedIsMarkedAndTheAcknowledgementGoesBack()
    {
        Reservation booked = ReservationFactory.Planned();

        booked.Disappear();

        Assert.True(booked.EpgMissing);
        Assert.Null(booked.AcknowledgedAt);
    }

    [Fact]
    public void ABroadcastTheGuideAnnouncesAgainCarriesNoMarkSayingItIsMissing()
    {
        Reservation booked = ReservationFactory.Planned();
        booked.Disappear();
        booked.Acknowledge(ReservationFactory.Now);

        booked.Reappear();

        Assert.False(booked.EpgMissing);
        Assert.Null(booked.AcknowledgedAt);
    }

    [Fact]
    public void ABroadcastAskedForASecondBeforeItStartsKeepsItsOwnStartAndLosesOnlyTheMargin()
    {
        DateTime asking = Airs.AddSeconds(-1);
        Reservation asked = PlannedAround(asking);

        Assert.Equal(Airs, asked.StartAt);
        Assert.Equal(TimeSpan.FromSeconds(1), asked.MarginBefore.Value);
        Assert.Equal(asking, asked.EffectiveStartAt);
    }

    [Fact]
    public void ABroadcastAskedForAtTheInstantItStartsKeepsItsOwnStartAndLosesAllOfTheMargin()
    {
        Reservation asked = PlannedAround(Airs);

        Assert.Equal(Airs, asked.StartAt);
        Assert.Equal(TimeSpan.Zero, asked.MarginBefore.Value);
        Assert.Equal(Airs, asked.EffectiveStartAt);
    }

    [Fact]
    public void ABroadcastAskedForASecondAfterItStartsIsPromisedFromTheMomentItWasAskedFor()
    {
        DateTime asking = Airs.AddSeconds(1);
        Reservation asked = PlannedAround(asking);

        Assert.Equal(asking, asked.StartAt);
        Assert.Equal(TimeSpan.Zero, asked.MarginBefore.Value);
        Assert.Equal(asking, asked.EffectiveStartAt);
    }

    [Fact]
    public void ABroadcastFiftyMinutesUnderWayIsPromisedFromTheMomentItWasAskedFor()
    {
        DateTime asking = Airs.AddMinutes(50);
        Reservation asked = PlannedAround(asking);

        Assert.Equal(asking, asked.StartAt);
        Assert.Equal(asking, asked.EffectiveStartAt);
    }

    [Fact]
    public void TheEndOfTheWindowIsWhereItWasWhateverTheClampDidToTheHead()
    {
        Reservation asked = PlannedAround(Airs.AddMinutes(50));

        Assert.Equal(Ends, asked.EndAt);
        Assert.Equal(Behind.Value, asked.MarginAfter.Value);
        Assert.Equal(Ends + Behind.Value, asked.EffectiveEndAt);
    }

    [Fact]
    public void TheBroadcastGoesOnSayingWhenItReallyStarted()
    {
        Reservation asked = PlannedAround(Airs.AddMinutes(50));

        Assert.Equal(Airs, asked.ProgrammeStartsAt);
        Assert.Equal(Airs, asked.Programme.StartsAt);
    }

    [Fact]
    public void ABroadcastAskedForASecondBeforeItEndsIsStillPromisedFromTheMomentItWasAskedFor()
    {
        DateTime asking = Ends.AddSeconds(-1);
        Reservation asked = PlannedAround(asking);

        Assert.Equal(asking, asked.StartAt);
        Assert.Equal(asking, asked.EffectiveStartAt);
    }

    [Fact]
    public void ABroadcastAskedForAtTheInstantItEndsIsLeftAsItWasAsked()
    {
        Reservation asked = PlannedAround(Ends);

        Assert.Equal(Airs, asked.StartAt);
        Assert.Equal(Ahead.Value, asked.MarginBefore.Value);
    }

    [Fact]
    public void ABroadcastAskedForASecondAfterItEndsIsLeftAsItWasAsked()
    {
        Reservation asked = PlannedAround(Ends.AddSeconds(1));

        Assert.Equal(Airs, asked.StartAt);
        Assert.Equal(Ahead.Value, asked.MarginBefore.Value);
    }

    [Fact]
    public void AMarginReachingToASecondAfterTheAskingIsKeptWhole()
    {
        Reservation asked = PlannedAround(Airs - Ahead.Value - TimeSpan.FromSeconds(1));

        Assert.Equal(Airs, asked.StartAt);
        Assert.Equal(Ahead.Value, asked.MarginBefore.Value);
        Assert.Equal(Airs - Ahead.Value, asked.EffectiveStartAt);
    }

    [Fact]
    public void AMarginReachingExactlyToTheAskingIsKeptWhole()
    {
        Reservation asked = PlannedAround(Airs - Ahead.Value);

        Assert.Equal(Airs, asked.StartAt);
        Assert.Equal(Ahead.Value, asked.MarginBefore.Value);
        Assert.Equal(Airs - Ahead.Value, asked.EffectiveStartAt);
    }

    [Fact]
    public void AMarginReachingToASecondBeforeTheAskingIsTrimmedBackToIt()
    {
        DateTime asking = Airs - Ahead.Value + TimeSpan.FromSeconds(1);
        Reservation asked = PlannedAround(asking);

        Assert.Equal(Airs, asked.StartAt);
        Assert.Equal(Ahead.Value - TimeSpan.FromSeconds(1), asked.MarginBefore.Value);
        Assert.Equal(asking, asked.EffectiveStartAt);
    }

    [Fact]
    public void ATrimmedMarginLandsOnTheWholeSecondAfterTheAskingRatherThanBeforeIt()
    {
        DateTime asking = Airs - Ahead.Value + TimeSpan.FromMilliseconds(500);
        Reservation asked = PlannedAround(asking);

        Assert.Equal(Airs, asked.StartAt);
        Assert.Equal(Airs - Ahead.Value + TimeSpan.FromSeconds(1), asked.EffectiveStartAt);
    }

    [Theory]
    [InlineData(-3601)]
    [InlineData(-121)]
    [InlineData(-120)]
    [InlineData(-119)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3000)]
    [InlineData(11999)]
    public void TheHeadOfTheWindowNeverReachesBackBeforeTheAskingWhileTheWindowIsStillOpen(int secondsFromTheStart)
    {
        DateTime asking = Airs.AddSeconds(secondsFromTheStart);

        Assert.True(
            PlannedAround(asking).EffectiveStartAt >= asking,
            "a reservation promised a window that had already begun when it was made");
    }

    [Fact]
    public void AReservationBornOfARuleIsClampedJustAsOneMadeByHandIs()
    {
        DateTime asking = Airs.AddMinutes(50);
        Reservation byHand = PlannedAround(asking);
        Reservation byRule = PlannedAround(asking, RuleId.New());

        Assert.True(byRule.IsRuleBorn);
        Assert.Equal(byHand.StartAt, byRule.StartAt);
        Assert.Equal(byHand.MarginBefore.Value, byRule.MarginBefore.Value);
        Assert.Equal(byHand.EffectiveStartAt, byRule.EffectiveStartAt);
    }

    [Fact]
    public void AReservationReadBackKeepsThePromiseItWasMadeWithRatherThanBeingClampedAgain()
    {
        Reservation stored = Reservation.Rehydrate(
            ReservationId.New(),
            ProgrammeAiring(),
            null,
            Priority.Default,
            Airs,
            Ends,
            true,
            Ahead,
            Behind,
            SnapshotBeforeItAirs(),
            null,
            BroadcastGroupRole.Standalone,
            ReservationState.Scheduled,
            null,
            null,
            false,
            [],
            false,
            null,
            false,
            null,
            Airs.AddMinutes(50));

        Assert.Equal(Airs, stored.StartAt);
        Assert.Equal(Ahead.Value, stored.MarginBefore.Value);
        Assert.Equal(Airs - Ahead.Value, stored.EffectiveStartAt);
    }

    [Fact]
    public void APlanWithNoMarginAheadOfTheBroadcastIsRefusedEvenWhenTheClampWouldHaveDroppedIt()
    {
        Assert.Throws<ArgumentNullException>(() => Reservation.Plan(
            ReservationId.New(),
            ProgrammeAiring(),
            null,
            Priority.Default,
            Airs,
            Ends,
            true,
            null!,
            Behind,
            SnapshotBeforeItAirs(),
            null,
            BroadcastGroupRole.Standalone,
            Airs.AddMinutes(50)));
    }

    [Theory]
    [InlineData("startAt")]
    [InlineData("endAt")]
    [InlineData("at")]
    public void APlanWhoseInstantsAreNotAllInUtcIsRefusedRatherThanClamped(string parameter)
    {
        DateTime local = DateTime.SpecifyKind(
            parameter switch
            {
                "startAt" => Airs,
                "endAt" => Ends,
                _ => Airs.AddMinutes(50),
            },
            DateTimeKind.Local);

        Assert.Throws<ArgumentException>(() => PlannedAround(
            parameter is "at" ? local : Airs.AddMinutes(50),
            startAt: parameter is "startAt" ? local : Airs,
            endAt: parameter is "endAt" ? local : Ends));
    }

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

        booked.Retarget(primary, primary.StartsAt.AddHours(2), true, SnapshotOfThePrimaryListing(), [Carried()], Key, BroadcastGroupRole.MovementPrimary);

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

        booked.Retarget(primary, primary.StartsAt.AddHours(1), true, SnapshotOfThePrimaryListing(), [], Key, BroadcastGroupRole.MovementPrimary);

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
            SnapshotOfThePrimaryListing(),
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
            SnapshotOfThePrimaryListing(),
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
            SnapshotOfThePrimaryListing(),
            [Carried()],
            null,
            BroadcastGroupRole.MovementPrimary));
    }

    [Fact]
    public void BrRd010AListingThatEndsBeforeItStartsLeavesTheReservationWhereItWas()
    {
        Reservation booked = ReservationFactory.Planned();
        ProgrammeRef before = booked.Programme;
        ProgrammeRef primary = Elsewhere(booked.ProgrammeStartsAt);

        Assert.Throws<ArgumentException>(() => booked.Retarget(
            primary,
            primary.StartsAt,
            true,
            SnapshotOfThePrimaryListing(),
            [Carried()],
            Key,
            BroadcastGroupRole.MovementPrimary));

        Assert.Equal(before, booked.Programme);
        Assert.Equal(BroadcastGroupRole.Standalone, booked.BroadcastGroupRole);
    }

    [Fact]
    public void BrRd010AReservationStandingAsideForTheSameBroadcastIsCancelledForThatReason()
    {
        Reservation booked = ReservationFactory.Planned();

        booked.Cancel(ReservationCancellation.SameBroadcast);

        Assert.Equal(ReservationState.Cancelled, booked.State);
        Assert.Equal(ReservationCancellation.SameBroadcast, booked.Cancellation);
    }

    [Fact(DisplayName = "a reservation that says nothing about encoding asks for one")]
    public void AReservationThatSaysNothingAsksForAnEncode() => Assert.True(PlannedAtNoon().EncodeWhenRecorded);

    [Fact(DisplayName = "a reservation can be planned asking for no encode")]
    public void AReservationCanBePlannedAskingForNoEncode()
        => Assert.False(PlannedAtNoon(encodeWhenRecorded: false).EncodeWhenRecorded);

    [Fact(DisplayName = "what a reservation asks for is changed without touching anything else about it")]
    public void WhatAReservationAsksForIsChangedOnItsOwn()
    {
        Reservation reservation = PlannedAtNoon();

        reservation.Rewish(false);

        Assert.False(reservation.EncodeWhenRecorded);
        Assert.Equal(Priority.Default, reservation.Priority);
        Assert.Equal(Margin.None, reservation.MarginBefore);
        Assert.Equal(ReservationState.Scheduled, reservation.State);

        reservation.Rewish(true);

        Assert.True(reservation.EncodeWhenRecorded);
    }

    [Fact]
    public void AReservationKeepsTheSoundItsSnapshotWasTakenWith()
    {
        Reservation reservation = Reservation.Plan(
            ReservationId.New(),
            ReservationFactory.Programme(),
            null,
            Priority.Default,
            Now.AddHours(2),
            Now.AddHours(3),
            true,
            Margin.None,
            Margin.None,
            new ProgrammeSnapshot("A programme", string.Empty, string.Empty, [], Now, AudioMode.DualMono, 2),
            null,
            BroadcastGroupRole.Standalone,
            Now);

        Assert.Equal(AudioMode.DualMono, reservation.SnapshotAudio);
        Assert.Equal(2, reservation.SnapshotSounds);
    }

    [Fact]
    public void AReservationThatFollowsItsProgrammeTakesTheSoundTheGuideNowAnnounces()
    {
        Reservation reservation = ReservationFactory.Planned();

        reservation.Follow(
            Now.AddHours(3),
            Now.AddHours(4),
            true,
            new ProgrammeSnapshot("A programme", string.Empty, string.Empty, [], Now, AudioMode.DualMono, 2),
            [
                new EpgDivergence(
                    DivergedField.StartAt,
                    reservation.ProgrammeStartsAt.ToString("O"),
                    Now.AddHours(3).ToString("O"),
                    Now),
            ]);

        Assert.Equal(AudioMode.DualMono, reservation.SnapshotAudio);
        Assert.Equal(2, reservation.SnapshotSounds);
    }

    private static Programme Listing(int eventId, DateTime startsAt, DateTime? endsAt)
        => Domain.Programmes.Programme.Rehydrate(
            new ProgrammeId(new NetworkId(32736), new ServiceId(1024), new EventId(eventId)),
            new TransportStreamId(32736),
            startsAt,
            endsAt,
            $"A programme {eventId}",
            string.Empty,
            false,
            ReservationFactory.Now);

    private static EpgDivergence Slipped(Reservation reservation)
        => new(
            DivergedField.StartAt,
            reservation.ProgrammeStartsAt.ToString("O"),
            reservation.ProgrammeStartsAt.AddMinutes(30).ToString("O"),
            Later);

    private static ProgrammeSnapshot SnapshotFromTheGuideLater(string name = "A programme")
        => new(
            name,
            "What it is about",
            string.Empty,
            [new ProgrammeGenre(7, 1)],
            Later,
            AudioMode.Undetermined,
            ProgrammeSnapshot.SoundsUnannounced);

    private static Reservation PlannedAround(
        DateTime at,
        RuleId? ruleId = null,
        DateTime? startAt = null,
        DateTime? endAt = null)
        => Reservation.Plan(
            ReservationId.New(),
            ProgrammeAiring(),
            ruleId,
            Priority.Default,
            startAt ?? Airs,
            endAt ?? Ends,
            true,
            Ahead,
            Behind,
            SnapshotBeforeItAirs(),
            null,
            BroadcastGroupRole.Standalone,
            at);

    private static ProgrammeRef ProgrammeAiring()
        => new(new NetworkId(32736), new ServiceId(1024), new EventId(4001), Airs);

    private static ProgrammeSnapshot SnapshotBeforeItAirs()
        => new(
            "A programme",
            "What it is about",
            string.Empty,
            [new ProgrammeGenre(7, 1)],
            Airs.AddHours(-6),
            AudioMode.Undetermined,
            ProgrammeSnapshot.SoundsUnannounced);

    private static ProgrammeRef Elsewhere(DateTime startsAt)
        => new(new NetworkId(32736), new ServiceId(1025), new EventId(5001), startsAt);

    private static EpgDivergence Carried()
        => new(DivergedField.Service, "32736-1024", "32736-1025", Later);

    private static ProgrammeSnapshot SnapshotOfThePrimaryListing()
        => new(
            "The primary listing",
            "What it is about",
            string.Empty,
            [new ProgrammeGenre(7, 1)],
            Later,
            AudioMode.Undetermined,
            ProgrammeSnapshot.SoundsUnannounced);

    private static Reservation PlannedAtNoon(bool encodeWhenRecorded = true)
        => Reservation.Plan(
            ReservationId.New(),
            new ProgrammeRef(new NetworkId(32736), new ServiceId(1024), new EventId(4001), Noon.AddHours(2)),
            null,
            Priority.Default,
            Noon.AddHours(2),
            Noon.AddHours(3),
            true,
            Margin.None,
            Margin.None,
            SnapshotAtNoon(),
            null,
            BroadcastGroupRole.Standalone,
            Noon,
            encodeWhenRecorded);

    private static ProgrammeSnapshot SnapshotAtNoon()
        => new(
            "A programme",
            "What it is about",
            string.Empty,
            [],
            Noon,
            AudioMode.Undetermined,
            ProgrammeSnapshot.SoundsUnannounced);
}
