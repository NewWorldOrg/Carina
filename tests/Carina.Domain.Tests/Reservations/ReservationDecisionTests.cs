using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;

using static Carina.Domain.Tests.Reservations.ReservationFactory;

namespace Carina.Domain.Tests.Reservations;

public sealed class ReservationDecisionTests
{
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(10);

    [Fact(DisplayName = "BR-RS-001: a reservation being recorded is not revised, whatever the move")]
    public void AReservationBeingRecordedIsNotRevised()
    {
        Reservation claimed = Claimed();

        Assert.Equal(ReservationRevisionRefusal.AlreadyRecording, claimed.RefusalToRevise(ReservationMove.Keep, Now));
        Assert.Equal(ReservationRevisionRefusal.AlreadyRecording, claimed.RefusalToRevise(ReservationMove.Cancel, Now));
        Assert.Equal(ReservationRevisionRefusal.AlreadyRecording, claimed.RefusalToRevise(ReservationMove.Restore, Now));
    }

    [Theory(DisplayName = "BR-RS-001: only a reservation still waiting for its tuner is changed or cancelled")]
    [InlineData(ReservationState.Scheduled, null)]
    [InlineData(ReservationState.Conflict, null)]
    [InlineData(ReservationState.Cancelled, ReservationRevisionRefusal.NotStanding)]
    [InlineData(ReservationState.Missed, ReservationRevisionRefusal.NotStanding)]
    public void OnlyAReservationStillWaitingIsChanged(ReservationState state, ReservationRevisionRefusal? refused)
    {
        Reservation reservation = Rehydrated(state, null, null);

        Assert.Equal(refused, reservation.RefusalToRevise(ReservationMove.Keep, Now));
        Assert.Equal(refused, reservation.RefusalToRevise(ReservationMove.Cancel, Now));
    }

    [Theory(DisplayName = "BR-RS-001: only a cancelled reservation is restored")]
    [InlineData(ReservationState.Scheduled)]
    [InlineData(ReservationState.Conflict)]
    [InlineData(ReservationState.Missed)]
    public void OnlyACancelledReservationIsRestored(ReservationState state)
        => Assert.Equal(
            ReservationRevisionRefusal.NotCancelled,
            Rehydrated(state, null, null).RefusalToRevise(ReservationMove.Restore, Now));

    [Fact(DisplayName = "BR-RS-001: a cancelled reservation is restored until its window closes, and not from then on")]
    public void ACancelledReservationIsRestoredUntilItsWindowCloses()
    {
        Reservation cancelled = Rehydrated(ReservationState.Cancelled, null, null);

        Assert.Null(cancelled.RefusalToRevise(ReservationMove.Restore, cancelled.EffectiveEndAt.AddTicks(-1)));
        Assert.Equal(
            ReservationRevisionRefusal.AlreadyOver,
            cancelled.RefusalToRevise(ReservationMove.Restore, cancelled.EffectiveEndAt));
    }

    [Fact(DisplayName = "BR-RD-010: a reservation is yet to begin while it holds no tuner and its window has not opened")]
    public void AReservationIsYetToBeginBeforeItsWindowOpens()
    {
        Reservation waiting = Planned(marginBefore: Margin.OfSeconds(60));

        Assert.True(waiting.IsYetToBeginAt(waiting.EffectiveStartAt.AddTicks(-1)));
        Assert.False(waiting.IsYetToBeginAt(waiting.EffectiveStartAt));
        Assert.False(Claimed(Now.AddHours(-1)).IsYetToBeginAt(Now.AddHours(-2)));
    }

    [Fact(DisplayName = "BR-RD-010: a reservation already holding the key and the role it is placed in is not regrouped")]
    public void AReservationAlreadyGroupedSoIsNotRegrouped()
    {
        var key = new BroadcastGroupKey("relay:32736-1024-4001");
        Reservation grouped = Rehydrated(ReservationState.Scheduled, null, null, key, BroadcastGroupRole.RelaySegment);

        Assert.True(grouped.IsGroupedAs(new BroadcastGroupKey("relay:32736-1024-4001"), BroadcastGroupRole.RelaySegment));
        Assert.False(grouped.IsGroupedAs(key, BroadcastGroupRole.MovementSuppressed));
        Assert.False(grouped.IsGroupedAs(null, BroadcastGroupRole.Standalone));
        Assert.True(Planned().IsGroupedAs(null, BroadcastGroupRole.Standalone));
    }

    [Fact(DisplayName = "BR-RD-010: a segment reserved beside another keeps that reservation's rule, priority, margins and wish")]
    public void ASegmentReservedBesideAnotherFollowsItsExample()
    {
        var rule = RuleId.New();
        Reservation example = Reservation.Plan(
            ReservationId.New(),
            Programme(),
            rule,
            new Priority(7),
            Programme().StartsAt,
            Programme().StartsAt.AddHours(1),
            true,
            Margin.OfSeconds(30),
            Margin.OfSeconds(90),
            Snapshot(),
            new BroadcastGroupKey("relay:32736-1024-4001"),
            BroadcastGroupRole.RelaySegment,
            Now,
            encodeWhenRecorded: false);
        Programme later = Listing(4002, Now.AddHours(4), Now.AddHours(5));
        var sibling = new BroadcastTarget(later, new BroadcastGroupKey("relay:32736-1024-4001"), BroadcastGroupRole.RelaySegment);
        var id = ReservationId.New();

        Reservation beside = example.PlanBeside(id, sibling, Now);

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
        Programme later = Listing(4002, Now.AddHours(4), null);

        Reservation beside = Planned().PlanBeside(
            ReservationId.New(),
            new BroadcastTarget(later, new BroadcastGroupKey("relay:32736-1024-4001"), BroadcastGroupRole.RelaySegment),
            Now);

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
            Now);
        Reservation made = Reservation.Plan(
            ReservationId.New(),
            Programme(),
            rule.Id,
            new Priority(3),
            Programme().StartsAt,
            Programme().StartsAt.AddHours(1),
            true,
            Margin.OfSeconds(10),
            Margin.OfSeconds(20),
            Snapshot(),
            null,
            BroadcastGroupRole.Standalone,
            Now);

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
        Reservation gone = Rehydrated(
            ReservationState.Cancelled,
            null,
            null,
            cancellation: ReservationCancellation.ProgrammeGone,
            ruleId: rule);

        Assert.True(gone.MayBeRevivedByRule(gone.EffectiveEndAt.AddTicks(-1)));
        Assert.False(gone.MayBeRevivedByRule(gone.EffectiveEndAt));
        Assert.False(Rehydrated(ReservationState.Cancelled, null, null, cancellation: ReservationCancellation.ProgrammeGone)
            .MayBeRevivedByRule(Now));
        Assert.False(Rehydrated(ReservationState.Cancelled, null, null, cancellation: ReservationCancellation.ByHand, ruleId: rule)
            .MayBeRevivedByRule(Now));
        Assert.False(Rehydrated(ReservationState.Scheduled, null, null, ruleId: rule).MayBeRevivedByRule(Now));
    }

    [Theory(DisplayName = "BR-RS-003: a rule withdraws only a reservation it made that is secured or contended and not yet recording")]
    [InlineData(ReservationState.Scheduled, true)]
    [InlineData(ReservationState.Conflict, true)]
    [InlineData(ReservationState.Cancelled, false)]
    [InlineData(ReservationState.Missed, false)]
    public void ARuleWithdrawsOnlyWhatItMadeAndStillWaits(ReservationState state, bool withdrawn)
    {
        Reservation made = Rehydrated(state, null, null, ruleId: RuleId.New());

        Assert.Equal(withdrawn, made.MayBeWithdrawnByRule(RuleWithdrawal.WhenTheRuleIsDeleted, Grace, Now));
        Assert.Equal(withdrawn, made.MayBeWithdrawnByRule(RuleWithdrawal.WhenTheRuleIsSwitchedOff, Grace, Now));
    }

    [Fact(DisplayName = "BR-RS-003: a rule withdraws neither a reservation made by hand nor one being recorded")]
    public void ARuleWithdrawsNeitherAHandMadeNorARecordingReservation()
    {
        Reservation recording = Rehydrated(ReservationState.Scheduled, Now, null, ruleId: RuleId.New());

        Assert.False(Planned().MayBeWithdrawnByRule(RuleWithdrawal.WhenTheRuleIsDeleted, Grace, Now));
        Assert.False(recording.MayBeWithdrawnByRule(RuleWithdrawal.WhenTheRuleIsDeleted, Grace, Now));
    }

    [Fact(DisplayName = "BR-RS-003: a rule that still stands withdraws only what starts more than the grace away, and one switched off or deleted ignores the grace")]
    public void ARuleThatStillStandsLeavesWhatStartsWithinTheGrace()
    {
        Reservation made = Planned(ruleId: RuleId.New());
        DateTime atTheGrace = made.EffectiveStartAt - Grace;

        Assert.True(made.MayBeWithdrawnByRule(RuleWithdrawal.WhileTheRuleStands, Grace, atTheGrace.AddTicks(-1)));
        Assert.False(made.MayBeWithdrawnByRule(RuleWithdrawal.WhileTheRuleStands, Grace, atTheGrace));
        Assert.True(made.MayBeWithdrawnByRule(RuleWithdrawal.WhenTheRuleIsSwitchedOff, Grace, atTheGrace));
        Assert.True(made.MayBeWithdrawnByRule(RuleWithdrawal.WhenTheRuleIsDeleted, Grace, atTheGrace));
    }

    [Fact(DisplayName = "BR-RD-006: a programme is still to come until its end, or until the provisional length when no end is announced")]
    public void AProgrammeIsStillToComeUntilItsEnd()
    {
        Programme announced = Listing(4001, Now, Now.AddMinutes(30));
        Programme open = Listing(4002, Now, null);

        Assert.True(announced.IsStillToComeAt(Now.AddMinutes(30).AddTicks(-1)));
        Assert.False(announced.IsStillToComeAt(Now.AddMinutes(30)));
        Assert.True(open.IsStillToComeAt(Now + Reservation.ProvisionalLengthWhenTheEndIsNotAnnounced - TimeSpan.FromTicks(1)));
        Assert.False(open.IsStillToComeAt(Now + Reservation.ProvisionalLengthWhenTheEndIsNotAnnounced));
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
            Now);
}
