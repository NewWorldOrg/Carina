using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Reservations;

public sealed class TunerAllocationPlannerTests
{
    private static readonly DateTime Now = ReservationFactory.Now;

    private static readonly TuningParameters Terrestrial27 = TuningParameters.Terrestrial(27);

    private static readonly TuningParameters Terrestrial29 = TuningParameters.Terrestrial(29);

    private static readonly TuningParameters Terrestrial31 = TuningParameters.Terrestrial(31);

    private static readonly TuningParameters Terrestrial33 = TuningParameters.Terrestrial(33);

    private static readonly TuningParameters BsSlotOneStream = TuningParameters.Bs(15, new TransportStreamId(16625));

    private static readonly TuningParameters BsSlotOtherStream = TuningParameters.Bs(15, new TransportStreamId(16626));

    private static readonly TuningParameters Cs110 = TuningParameters.Cs110(4);

    [Fact]
    public void NothingToPlanIsAPlanThatSaysNothing()
    {
        AllocationPlan plan = Planned([], Capacity(TunerKind.Terrestrial));

        Assert.Empty(plan.Decisions);
        Assert.Empty(plan.Contended);
    }

    [Fact]
    public void EveryCandidateHandedInIsAnsweredInPlanningOrder()
    {
        AllocationCandidate first = Candidate(Terrestrial27, eventId: 4001);
        AllocationCandidate second = Candidate(Terrestrial29, eventId: 4002);
        AllocationCandidate third = Candidate(null, eventId: 4003);

        AllocationPlan plan = Planned([third, first, second], Capacity(TunerKind.Terrestrial));

        Assert.Equal(3, plan.Decisions.Count);
        Assert.Equal([first.Id, second.Id, third.Id], plan.Decisions.Select(decision => decision.Id));
    }

    [Fact]
    public void ThePlanSaysNothingAboutAReservationItNeverSaw()
    {
        AllocationPlan plan = Planned([Candidate(Terrestrial27)], Capacity(TunerKind.Terrestrial));

        Assert.Throws<KeyNotFoundException>(() => plan.For(ReservationId.New()));
    }

    [Fact]
    public void APlanAnswersEachReservationOnce()
    {
        ReservationId shared = new(Guid.Parse("00000000-0000-0000-0000-000000000001"));

        ArgumentException refused = Assert.Throws<ArgumentException>(() => Planned(
            [Candidate(Terrestrial27, id: shared, eventId: 4001), Candidate(Terrestrial29, id: shared, eventId: 4002)],
            Capacity(TunerKind.Terrestrial)));

        Assert.Equal("decisions", refused.ParamName);
    }

    [Fact]
    public void ThePlannerIsHandedTheCandidates()
    {
        ArgumentNullException refused = Assert.Throws<ArgumentNullException>(() => TunerAllocationPlanner.Plan(
            null!,
            Capacity(TunerKind.Terrestrial),
            RollingHorizon.Default,
            Now));

        Assert.Equal("candidates", refused.ParamName);
    }

    [Fact]
    public void ThePlannerIsHandedTheCapacity()
    {
        ArgumentNullException refused = Assert.Throws<ArgumentNullException>(() => TunerAllocationPlanner.Plan(
            [],
            null!,
            RollingHorizon.Default,
            Now));

        Assert.Equal("capacity", refused.ParamName);
    }

    [Fact]
    public void ThePlannerIsHandedTheHorizon()
    {
        ArgumentNullException refused = Assert.Throws<ArgumentNullException>(() => TunerAllocationPlanner.Plan(
            [],
            Capacity(TunerKind.Terrestrial),
            null!,
            Now));

        Assert.Equal("horizon", refused.ParamName);
    }

    [Fact]
    public void ThePlannerIsToldTheMomentInUtc()
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(() => TunerAllocationPlanner.Plan(
            [],
            Capacity(TunerKind.Terrestrial),
            RollingHorizon.Default,
            new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Local)));

        Assert.Equal("at", refused.ParamName);
    }

    [Fact]
    public void TheHigherPriorityTakesTheOnlyTuner()
    {
        AllocationCandidate wanted = Candidate(Terrestrial27, priority: 20, eventId: 4001);
        AllocationCandidate lost = Candidate(Terrestrial29, priority: 10, eventId: 4002);

        AllocationPlan plan = Planned([lost, wanted], Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, wanted));
        Assert.Equal(AllocationVerdict.Contended, Verdict(plan, lost));
    }

    [Fact]
    public void TheEarlierStartTakesTheOnlyTunerWhenThePrioritiesAreEqual()
    {
        AllocationCandidate earlier = Candidate(Terrestrial27, fromMinutes: 0, eventId: 4002);
        AllocationCandidate later = Candidate(Terrestrial29, fromMinutes: 10, eventId: 4001);

        AllocationPlan plan = Planned([later, earlier], Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, earlier));
        Assert.Equal(AllocationVerdict.Contended, Verdict(plan, later));
    }

    [Fact]
    public void PriorityOutranksTheEarlierStart()
    {
        AllocationCandidate later = Candidate(Terrestrial27, priority: 20, fromMinutes: 10, eventId: 4002);
        AllocationCandidate earlier = Candidate(Terrestrial29, priority: 10, fromMinutes: 0, eventId: 4001);

        AllocationPlan plan = Planned([earlier, later], Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, later));
        Assert.Equal(AllocationVerdict.Contended, Verdict(plan, earlier));
    }

    [Theory]
    [InlineData(32737, 1024, 4001, 0)]
    [InlineData(32736, 1025, 4001, 0)]
    [InlineData(32736, 1024, 4002, 0)]
    [InlineData(32736, 1024, 4001, 1)]
    public void TheBroadcastIdentitySettlesWhatPriorityAndStartCannot(
        int networkId,
        int serviceId,
        int eventId,
        int startsAtOffsetMinutes)
    {
        AllocationCandidate winner = Candidate(
            Terrestrial27,
            id: new ReservationId(Guid.Parse("00000000-0000-0000-0000-000000000009")),
            programme: Programme(32736, 1024, 4001, 0));
        AllocationCandidate loser = Candidate(
            Terrestrial29,
            id: new ReservationId(Guid.Parse("00000000-0000-0000-0000-000000000001")),
            programme: Programme(networkId, serviceId, eventId, startsAtOffsetMinutes));

        AllocationPlan plan = Planned([loser, winner], Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, winner));
        Assert.Equal(AllocationVerdict.Contended, Verdict(plan, loser));
    }

    [Fact]
    public void TheStartOutranksTheBroadcastIdentity()
    {
        AllocationCandidate earlier = Candidate(Terrestrial27, fromMinutes: 0, eventId: 4009);
        AllocationCandidate later = Candidate(Terrestrial29, fromMinutes: 10, eventId: 4001);

        AllocationPlan plan = Planned([later, earlier], Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, earlier));
        Assert.Equal(AllocationVerdict.Contended, Verdict(plan, later));
    }

    [Fact]
    public void TheIdentifierSettlesWhatTheBroadcastIdentityCannot()
    {
        ProgrammeRef shared = Programme(32736, 1024, 4001, 0);
        AllocationCandidate lower = Candidate(
            Terrestrial27,
            id: new ReservationId(Guid.Parse("00000000-0000-0000-0000-000000000001")),
            programme: shared);
        AllocationCandidate higher = Candidate(
            Terrestrial29,
            id: new ReservationId(Guid.Parse("00000000-0000-0000-0000-000000000002")),
            programme: shared);

        AllocationPlan plan = Planned([higher, lower], Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, lower));
        Assert.Equal(AllocationVerdict.Contended, Verdict(plan, higher));
    }

    [Fact]
    public void TheBroadcastIdentityOutranksTheIdentifier()
    {
        AllocationCandidate lowerEvent = Candidate(
            Terrestrial27,
            id: new ReservationId(Guid.Parse("00000000-0000-0000-0000-000000000009")),
            eventId: 4001);
        AllocationCandidate higherEvent = Candidate(
            Terrestrial29,
            id: new ReservationId(Guid.Parse("00000000-0000-0000-0000-000000000001")),
            eventId: 4002);

        AllocationPlan plan = Planned([higherEvent, lowerEvent], Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, lowerEvent));
        Assert.Equal(AllocationVerdict.Contended, Verdict(plan, higherEvent));
    }

    [Fact]
    public void TheSameCandidatesInAnyOrderGiveTheSameAnswer()
    {
        AllocationCandidate first = Candidate(Terrestrial27, eventId: 4001);
        AllocationCandidate second = Candidate(Terrestrial29, eventId: 4002);
        AllocationCandidate third = Candidate(Terrestrial31, eventId: 4003);
        TunerCapacity capacity = Capacity(TunerKind.Terrestrial, TunerKind.Terrestrial);

        string forwards = Describe(Planned([first, second, third], capacity));
        string backwards = Describe(Planned([third, second, first], capacity));
        string shuffled = Describe(Planned([second, third, first], capacity));

        Assert.Equal($"{first.Id}=Secured;{second.Id}=Secured;{third.Id}=Contended", forwards);
        Assert.Equal(forwards, backwards);
        Assert.Equal(forwards, shuffled);
    }

    [Theory]
    [InlineData(-1, AllocationVerdict.Contended)]
    [InlineData(0, AllocationVerdict.Secured)]
    [InlineData(1, AllocationVerdict.Secured)]
    public void AWindowIsHeldFromItsStartUpToButNotIncludingItsEnd(int ticksAfterTheEnd, AllocationVerdict expected)
    {
        AllocationCandidate first = Candidate(Terrestrial27, from: Now, to: Now.AddMinutes(60), eventId: 4001);
        AllocationCandidate second = Candidate(
            Terrestrial29,
            from: Now.AddMinutes(60).AddTicks(ticksAfterTheEnd),
            to: Now.AddMinutes(120),
            eventId: 4002);

        AllocationPlan plan = Planned([first, second], Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, first));
        Assert.Equal(expected, Verdict(plan, second));
    }

    [Fact]
    public void TheMarginsAreWhatMakeAnEndAndAStartAtTheSameMinuteCollide()
    {
        Reservation ends = ReservationFactory.Planned(
            priority: new Priority(20),
            programme: ReservationFactory.Programme(4001, Now.AddHours(-1)),
            marginAfter: Margin.OfSeconds(30));
        Reservation begins = ReservationFactory.Planned(
            priority: new Priority(10),
            programme: ReservationFactory.Programme(4002, Now),
            marginBefore: Margin.OfSeconds(10));

        AllocationPlan plan = Planned(
            [AllocationCandidate.Of(ends, Terrestrial27), AllocationCandidate.Of(begins, Terrestrial29)],
            Capacity(TunerKind.Terrestrial));

        Assert.Equal(Now, ends.EndAt);
        Assert.Equal(Now, begins.StartAt);
        Assert.Equal(AllocationVerdict.Secured, plan.For(ends.Id).Verdict);
        Assert.Equal(AllocationVerdict.Contended, plan.For(begins.Id).Verdict);
    }

    [Fact]
    public void WithoutMarginsThatSameEndAndStartDoNotCollide()
    {
        Reservation ends = ReservationFactory.Planned(programme: ReservationFactory.Programme(4001, Now.AddHours(-1)));
        Reservation begins = ReservationFactory.Planned(programme: ReservationFactory.Programme(4002, Now));

        AllocationPlan plan = Planned(
            [AllocationCandidate.Of(ends, Terrestrial27), AllocationCandidate.Of(begins, Terrestrial29)],
            Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Secured, plan.For(ends.Id).Verdict);
        Assert.Equal(AllocationVerdict.Secured, plan.For(begins.Id).Verdict);
    }

    [Fact]
    public void ThreeProgrammesOnOneChannelRideOneTuner()
    {
        AllocationCandidate first = Candidate(Terrestrial27, eventId: 4001);
        AllocationCandidate second = Candidate(Terrestrial27, eventId: 4002);
        AllocationCandidate third = Candidate(Terrestrial27, eventId: 4003);

        AllocationPlan plan = Planned([first, second, third], Capacity(TunerKind.Terrestrial));

        Assert.Equal(3, plan.Decisions.Count);
        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, first));
        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, second));
        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, third));
    }

    [Fact]
    public void AnotherChannelIsAnotherTuner()
    {
        AllocationCandidate first = Candidate(Terrestrial27, eventId: 4001);
        AllocationCandidate second = Candidate(Terrestrial29, eventId: 4002);

        AllocationPlan plan = Planned([first, second], Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, first));
        Assert.Equal(AllocationVerdict.Contended, Verdict(plan, second));
    }

    [Fact]
    public void TheTwoStreamsCarriedOnOneSatelliteSlotAreTwoTuners()
    {
        AllocationCandidate first = Candidate(BsSlotOneStream, eventId: 4001);
        AllocationCandidate second = Candidate(BsSlotOtherStream, eventId: 4002);

        AllocationPlan plan = Planned([first, second], Capacity(TunerKind.Satellite));

        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, first));
        Assert.Equal(AllocationVerdict.Contended, Verdict(plan, second));
    }

    [Fact]
    public void TheSameStreamOnOneSatelliteSlotIsOneTuner()
    {
        AllocationCandidate first = Candidate(BsSlotOneStream, eventId: 4001);
        AllocationCandidate second = Candidate(BsSlotOneStream, eventId: 4002);

        AllocationPlan plan = Planned([first, second], Capacity(TunerKind.Satellite));

        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, first));
        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, second));
    }

    [Fact]
    public void OneSatelliteTunerCannotOpenBothSatelliteSystemsAtOnce()
    {
        AllocationCandidate broadcasting = Candidate(BsSlotOneStream, priority: 20, eventId: 4001);
        AllocationCandidate communication = Candidate(Cs110, priority: 10, eventId: 4002);

        AllocationPlan plan = Planned([communication, broadcasting], Capacity(TunerKind.Satellite));

        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, broadcasting));
        Assert.Equal(AllocationVerdict.Contended, Verdict(plan, communication));
    }

    [Fact]
    public void TwoSatelliteTunersAreNotFourWhenTheSystemsAreCountedApart()
    {
        AllocationCandidate first = Candidate(BsSlotOneStream, priority: 30, eventId: 4001);
        AllocationCandidate second = Candidate(BsSlotOtherStream, priority: 20, eventId: 4002);
        AllocationCandidate third = Candidate(Cs110, priority: 10, eventId: 4003);

        AllocationPlan plan = Planned(
            [third, second, first],
            Capacity(TunerKind.Satellite, TunerKind.Satellite));

        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, first));
        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, second));
        Assert.Equal(AllocationVerdict.Contended, Verdict(plan, third));
    }

    [Fact]
    public void TwoSatelliteTunersTakeOneOfEachSatelliteSystem()
    {
        AllocationCandidate broadcasting = Candidate(BsSlotOneStream, eventId: 4001);
        AllocationCandidate communication = Candidate(Cs110, eventId: 4002);

        AllocationPlan plan = Planned(
            [broadcasting, communication],
            Capacity(TunerKind.Satellite, TunerKind.Satellite));

        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, broadcasting));
        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, communication));
    }

    [Fact]
    public void ATerrestrialTunerIsNoUseToASatelliteProgramme()
    {
        AllocationCandidate satellite = Candidate(BsSlotOneStream, eventId: 4001);

        AllocationPlan plan = Planned([satellite], Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Contended, Verdict(plan, satellite));
        Assert.Empty(plan.For(satellite.Id).Instead);
    }

    [Fact]
    public void AFaultedSeatCountsOrNotAccordingToTheCapacityHandedIn()
    {
        AllocationCandidate first = Candidate(Terrestrial27, priority: 20, eventId: 4001);
        AllocationCandidate second = Candidate(Terrestrial29, priority: 10, eventId: 4002);
        TunerCapacity withAFaultedSeat = new(
            [
                new TunerSeat("seat0", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false),
                new TunerSeat("seat1", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: true),
            ],
            []);

        Assert.Equal(
            AllocationVerdict.Secured,
            Verdict(Planned([first, second], withAFaultedSeat), second));
        Assert.Equal(
            AllocationVerdict.Contended,
            Verdict(Planned([first, second], withAFaultedSeat.Healthy), second));
    }

    [Fact]
    public void WhatIsBeingRecordedKeepsItsTunerHoweverLowItsPriority()
    {
        AllocationCandidate recording = Candidate(Terrestrial27, priority: 1, pinned: true, eventId: 4001);
        AllocationCandidate wanted = Candidate(Terrestrial29, priority: 99, eventId: 4002);

        AllocationPlan plan = Planned([recording, wanted], Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Pinned, Verdict(plan, recording));
        Assert.Equal(AllocationVerdict.Contended, Verdict(plan, wanted));
    }

    [Fact]
    public void WhatIsBeingRecordedIsCountedEvenThoughItIsNotBeingDecided()
    {
        AllocationCandidate recording = Candidate(Terrestrial27, pinned: true, eventId: 4001);
        AllocationCandidate wanted = Candidate(Terrestrial29, eventId: 4002);

        Assert.Equal(
            AllocationVerdict.Contended,
            Verdict(Planned([recording, wanted], Capacity(TunerKind.Terrestrial)), wanted));
        Assert.Equal(
            AllocationVerdict.Secured,
            Verdict(Planned([wanted], Capacity(TunerKind.Terrestrial)), wanted));
    }

    [Fact]
    public void MoreRecordingsThanTunersAreAllStillRecording()
    {
        AllocationCandidate first = Candidate(Terrestrial27, pinned: true, eventId: 4001);
        AllocationCandidate second = Candidate(Terrestrial29, pinned: true, eventId: 4002);
        AllocationCandidate wanted = Candidate(Terrestrial31, eventId: 4003);

        AllocationPlan plan = Planned([first, second, wanted], Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Pinned, Verdict(plan, first));
        Assert.Equal(AllocationVerdict.Pinned, Verdict(plan, second));
        Assert.Equal(AllocationVerdict.Contended, Verdict(plan, wanted));
    }

    [Fact]
    public void ATunerShortageAtAnotherHourDoesNotContendThisOne()
    {
        AllocationCandidate first = Candidate(Terrestrial27, pinned: true, eventId: 4001);
        AllocationCandidate second = Candidate(Terrestrial29, pinned: true, eventId: 4002);
        AllocationCandidate later = Candidate(
            Terrestrial31,
            from: Now.AddHours(2),
            to: Now.AddHours(3),
            eventId: 4003);

        AllocationPlan plan = Planned([first, second, later], Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, later));
    }

    [Fact]
    public void ARecordingWhoseChannelCannotBeResolvedIsStillRecording()
    {
        AllocationCandidate recording = Candidate(null, pinned: true, eventId: 4001);
        AllocationCandidate wanted = Candidate(Terrestrial29, eventId: 4002);

        AllocationPlan plan = Planned([recording, wanted], Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Pinned, Verdict(plan, recording));
        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, wanted));
    }

    [Fact]
    public void AServiceWithNowhereToTuneIsNeverSecured()
    {
        AllocationCandidate nowhere = Candidate(null, eventId: 4001);
        AllocationCandidate elsewhere = Candidate(Terrestrial29, eventId: 4002);

        AllocationPlan plan = Planned([nowhere, elsewhere], Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Unreachable, Verdict(plan, nowhere));
        Assert.False(plan.For(nowhere.Id).KeepsATuner);
        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, elsewhere));
    }

    [Fact]
    public void ARecordingWithNoAnnouncedEndHoldsItsTunerToTheHorizon()
    {
        AllocationPlan plan = Planned(
            [Unfinished(pinned: true), Wanting()],
            Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Contended, Verdict(plan, Wanting()));
    }

    [Fact]
    public void ARecordingWithNoAnnouncedEndHoldsItsTunerThroughTheReservationItStandsFor()
    {
        Reservation recording = ReservationFactory.Claimed();
        recording.Reframe(Now.AddMinutes(-30), Now.AddMinutes(1), endAtConfirmed: false);
        Reservation wanted = ReservationFactory.Planned(programme: ReservationFactory.Programme(4002));
        wanted.Reframe(Now.AddMinutes(10), Now.AddMinutes(70), endAtConfirmed: true);

        AllocationPlan plan = Planned(
            [AllocationCandidate.Of(recording, Terrestrial27), AllocationCandidate.Of(wanted, Terrestrial29)],
            Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Pinned, plan.For(recording.Id).Verdict);
        Assert.Equal(AllocationVerdict.Contended, plan.For(wanted.Id).Verdict);
    }

    [Fact]
    public void AnAnnouncedEndIsNotPushedForward()
    {
        AllocationCandidate recording = Candidate(
            Terrestrial27,
            from: Now.AddMinutes(-30),
            to: Now.AddMinutes(5),
            pinned: true,
            eventId: 4001);

        AllocationPlan plan = Planned([recording, Wanting()], Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, Wanting()));
    }

    [Fact]
    public void AReservationWithNoAnnouncedEndThatHasNotStartedKeepsItsEstimate()
    {
        AllocationPlan plan = Planned(
            [Unfinished(pinned: false), Wanting()],
            Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, Wanting()));
    }

    [Fact]
    public void TheHorizonNeverPullsAnEndBackwards()
    {
        AllocationCandidate recording = Candidate(
            Terrestrial27,
            from: Now.AddMinutes(-30),
            to: Now.AddMinutes(120),
            endAtConfirmed: false,
            pinned: true,
            eventId: 4001);
        AllocationCandidate wanted = Candidate(
            Terrestrial29,
            from: Now.AddMinutes(60),
            to: Now.AddMinutes(90),
            eventId: 4002);

        AllocationPlan plan = Planned([recording, wanted], Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Contended, Verdict(plan, wanted));
    }

    [Theory]
    [InlineData(-1, AllocationVerdict.Contended)]
    [InlineData(0, AllocationVerdict.Secured)]
    [InlineData(1, AllocationVerdict.Secured)]
    public void TheHorizonReachesUpToButNotIncludingTheMomentItNames(
        int ticksAfterTheHorizon,
        AllocationVerdict expected)
    {
        DateTime horizonEnds = Now + RollingHorizon.Provisional;
        AllocationCandidate wanted = Candidate(
            Terrestrial29,
            from: horizonEnds.AddTicks(ticksAfterTheHorizon),
            to: horizonEnds.AddHours(1),
            eventId: 4002);

        AllocationPlan plan = Planned([Unfinished(pinned: true), wanted], Capacity(TunerKind.Terrestrial));

        Assert.Equal(expected, Verdict(plan, wanted));
    }

    [Fact]
    public void AShorterHorizonLetsTheNextRecordingIn()
    {
        AllocationCandidate[] candidates = [Unfinished(pinned: true), Wanting()];
        TunerCapacity capacity = Capacity(TunerKind.Terrestrial);

        Assert.Equal(
            AllocationVerdict.Contended,
            Verdict(
                TunerAllocationPlanner.Plan(candidates, capacity, RollingHorizon.Default, Now),
                Wanting()));
        Assert.Equal(
            AllocationVerdict.Secured,
            Verdict(
                TunerAllocationPlanner.Plan(candidates, capacity, new RollingHorizon(TimeSpan.FromMinutes(5)), Now),
                Wanting()));
    }

    [Fact]
    public void ARiderOnAChannelAlreadyOpenNeedsNoTunerOfItsOwn()
    {
        AllocationCandidate recording = Candidate(Terrestrial27, pinned: true, eventId: 4001);
        AllocationCandidate rider = Candidate(Terrestrial27, eventId: 4002);
        AllocationCandidate another = Candidate(Terrestrial29, eventId: 4003);

        AllocationPlan plan = Planned([recording, rider, another], Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Pinned, Verdict(plan, recording));
        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, rider));
        Assert.Equal(AllocationVerdict.Contended, Verdict(plan, another));
    }

    [Fact]
    public void ARiderIsSecuredEvenWhereTheRecordingsAlreadyOutnumberTheTuners()
    {
        AllocationCandidate first = Candidate(Terrestrial27, pinned: true, eventId: 4001);
        AllocationCandidate second = Candidate(Terrestrial29, pinned: true, eventId: 4002);
        AllocationCandidate rider = Candidate(Terrestrial27, eventId: 4003);

        AllocationPlan plan = Planned([first, second, rider], Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Pinned, Verdict(plan, first));
        Assert.Equal(AllocationVerdict.Pinned, Verdict(plan, second));
        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, rider));
    }

    [Fact]
    public void RidingAlongNeedsTheChannelToBeOpenAtThatMoment()
    {
        AllocationCandidate earlier = Candidate(
            Terrestrial27,
            priority: 30,
            from: Now,
            to: Now.AddMinutes(30),
            eventId: 4001);
        AllocationCandidate other = Candidate(
            Terrestrial29,
            priority: 20,
            from: Now.AddMinutes(30),
            to: Now.AddMinutes(60),
            eventId: 4002);
        AllocationCandidate late = Candidate(
            Terrestrial27,
            priority: 10,
            from: Now.AddMinutes(30),
            to: Now.AddMinutes(60),
            eventId: 4003);

        AllocationPlan plan = Planned([earlier, other, late], Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, earlier));
        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, other));
        Assert.Equal(AllocationVerdict.Contended, Verdict(plan, late));
    }

    [Fact]
    public void ARiderLosesTheChannelWhenWhatItRidesEndsFirst()
    {
        AllocationCandidate ridden = Candidate(
            Terrestrial27,
            from: Now,
            to: Now.AddMinutes(30),
            pinned: true,
            eventId: 4001);
        AllocationCandidate other = Candidate(
            Terrestrial29,
            from: Now,
            to: Now.AddMinutes(120),
            pinned: true,
            eventId: 4002);
        AllocationCandidate rider = Candidate(
            Terrestrial27,
            from: Now,
            to: Now.AddMinutes(120),
            eventId: 4003);

        AllocationPlan plan = Planned([ridden, other, rider], Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Pinned, Verdict(plan, ridden));
        Assert.Equal(AllocationVerdict.Pinned, Verdict(plan, other));
        Assert.Equal(AllocationVerdict.Contended, Verdict(plan, rider));
    }

    [Fact]
    public void ARiderThatOutlastsNothingKeepsTheChannelItRides()
    {
        AllocationCandidate ridden = Candidate(
            Terrestrial27,
            from: Now,
            to: Now.AddMinutes(120),
            pinned: true,
            eventId: 4001);
        AllocationCandidate other = Candidate(
            Terrestrial29,
            from: Now,
            to: Now.AddMinutes(120),
            pinned: true,
            eventId: 4002);
        AllocationCandidate rider = Candidate(
            Terrestrial27,
            from: Now,
            to: Now.AddMinutes(30),
            eventId: 4003);

        AllocationPlan plan = Planned([ridden, other, rider], Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, rider));
    }

    [Fact]
    public void WhereTwoSeatsAreShortOfThreeRecordingsTheyAreAllRecordedInstead()
    {
        AllocationCandidate first = Candidate(Terrestrial27, priority: 30, pinned: true, eventId: 4001);
        AllocationCandidate second = Candidate(Terrestrial29, priority: 20, pinned: true, eventId: 4002);
        AllocationCandidate third = Candidate(Terrestrial31, priority: 15, pinned: true, eventId: 4003);
        AllocationCandidate lost = Candidate(Terrestrial33, priority: 10, eventId: 4004);

        AllocationPlan plan = Planned(
            [first, second, third, lost],
            Capacity(TunerKind.Terrestrial, TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Contended, Verdict(plan, lost));
        Assert.Equal([first.Id, second.Id, third.Id], plan.For(lost.Id).Instead);
    }

    [Fact]
    public void ARecordingOnASystemWithNoSeatAtAllIsNotRecordedInstead()
    {
        AllocationCandidate satellite = Candidate(BsSlotOneStream, priority: 30, pinned: true, eventId: 4001);
        AllocationCandidate terrestrial = Candidate(Terrestrial27, priority: 20, pinned: true, eventId: 4002);
        AllocationCandidate lost = Candidate(Terrestrial29, priority: 10, eventId: 4003);

        AllocationPlan plan = Planned(
            [satellite, terrestrial, lost],
            Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Contended, Verdict(plan, lost));
        Assert.Equal([terrestrial.Id], plan.For(lost.Id).Instead);
    }

    [Fact]
    public void WhatWasRecordedInsteadIsWeighedOverTheLosersWindowAndNotItsOwn()
    {
        AllocationCandidate satellite = Candidate(
            BsSlotOneStream,
            priority: 30,
            from: Now.AddMinutes(60),
            to: Now.AddMinutes(90),
            eventId: 4001);
        AllocationCandidate taken = Candidate(
            Terrestrial27,
            priority: 20,
            from: Now,
            to: Now.AddMinutes(120),
            eventId: 4002);
        AllocationCandidate lost = Candidate(
            Terrestrial29,
            priority: 10,
            from: Now,
            to: Now.AddMinutes(120),
            eventId: 4003);

        AllocationPlan plan = Planned(
            [satellite, taken, lost],
            Capacity(TunerKind.Terrestrial, TunerKind.Satellite));

        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, satellite));
        Assert.Equal(AllocationVerdict.Contended, Verdict(plan, lost));
        Assert.Equal([taken.Id], plan.For(lost.Id).Instead);
    }

    [Fact]
    public void ARecordingThatWouldHaveLetTheSeatsFallIntoPlaceIsRecordedInstead()
    {
        AllocationCandidate communication = Candidate(Cs110, priority: 30, eventId: 4001);
        AllocationCandidate broadcasting = Candidate(BsSlotOneStream, priority: 20, eventId: 4002);
        AllocationCandidate lost = Candidate(Terrestrial27, priority: 10, eventId: 4003);
        TunerCapacity convertible = new(
            [
                new TunerSeat("seat0", [TuneSystem.IsdbSBs, TuneSystem.IsdbSCs110], Faulted: false),
                new TunerSeat("seat1", [TuneSystem.IsdbT, TuneSystem.IsdbSBs], Faulted: false),
            ],
            []);

        AllocationPlan plan = Planned([communication, broadcasting, lost], convertible);

        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, communication));
        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, broadcasting));
        Assert.Equal(AllocationVerdict.Contended, Verdict(plan, lost));
        Assert.Equal([communication.Id, broadcasting.Id], plan.For(lost.Id).Instead);
    }

    [Fact]
    public void ARecordingOnASeatTheLoserCouldNeverHaveTakenIsNotRecordedInstead()
    {
        AllocationCandidate satellite = Candidate(BsSlotOneStream, priority: 30, eventId: 4001);
        AllocationCandidate taken = Candidate(Terrestrial27, priority: 20, eventId: 4002);
        AllocationCandidate lost = Candidate(Terrestrial29, priority: 10, eventId: 4003);

        AllocationPlan plan = Planned(
            [satellite, taken, lost],
            Capacity(TunerKind.Terrestrial, TunerKind.Satellite));

        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, satellite));
        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, taken));
        Assert.Equal(AllocationVerdict.Contended, Verdict(plan, lost));
        Assert.Equal([taken.Id], plan.For(lost.Id).Instead);
    }

    [Fact]
    public void NothingIsRecordedInsteadOfWhatNoTunerCouldHaveTakenAtAll()
    {
        AllocationCandidate taken = Candidate(Terrestrial27, priority: 20, eventId: 4001);
        AllocationCandidate satellite = Candidate(BsSlotOneStream, priority: 10, eventId: 4002);

        AllocationPlan plan = Planned([taken, satellite], Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Contended, Verdict(plan, satellite));
        Assert.Empty(plan.For(satellite.Id).Instead);
    }

    [Fact]
    public void WhereNoOneSeatWouldHaveBeenEnoughTheyAreAllRecordedInstead()
    {
        AllocationCandidate first = Candidate(Terrestrial27, pinned: true, priority: 20, eventId: 4001);
        AllocationCandidate second = Candidate(Terrestrial29, pinned: true, priority: 15, eventId: 4002);
        AllocationCandidate lost = Candidate(Terrestrial31, priority: 10, eventId: 4003);

        AllocationPlan plan = Planned([first, second, lost], Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Contended, Verdict(plan, lost));
        Assert.Equal([first.Id, second.Id], plan.For(lost.Id).Instead);
    }

    [Fact]
    public void WhatWasRecordedInsteadIsNamedInPlanningOrder()
    {
        AllocationCandidate first = Candidate(Terrestrial27, priority: 30, eventId: 4001);
        AllocationCandidate second = Candidate(Terrestrial29, priority: 20, eventId: 4002);
        AllocationCandidate lost = Candidate(Terrestrial31, priority: 10, eventId: 4003);

        AllocationPlan plan = Planned(
            [lost, second, first],
            Capacity(TunerKind.Terrestrial, TunerKind.Terrestrial));

        Assert.Equal([first.Id, second.Id], plan.For(lost.Id).Instead);
    }

    [Fact]
    public void SomethingRidingTheSameChannelIsNotRecordedInstead()
    {
        AllocationCandidate rider = Candidate(
            Terrestrial27,
            priority: 30,
            from: Now,
            to: Now.AddMinutes(30),
            eventId: 4001);
        AllocationCandidate other = Candidate(
            Terrestrial29,
            priority: 20,
            from: Now.AddMinutes(60),
            to: Now.AddMinutes(90),
            eventId: 4002);
        AllocationCandidate lost = Candidate(
            Terrestrial27,
            priority: 10,
            from: Now,
            to: Now.AddMinutes(120),
            eventId: 4003);

        AllocationPlan plan = Planned([lost, other, rider], Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, rider));
        Assert.Equal(AllocationVerdict.Contended, Verdict(plan, lost));
        Assert.Equal([other.Id], plan.For(lost.Id).Instead);
    }

    [Fact]
    public void SomethingThatOnlyTouchesTheWindowIsNotRecordedInstead()
    {
        AllocationCandidate before = Candidate(
            Terrestrial27,
            priority: 40,
            from: Now.AddMinutes(-60),
            to: Now,
            eventId: 4001);
        AllocationCandidate after = Candidate(
            Terrestrial29,
            priority: 35,
            from: Now.AddMinutes(60),
            to: Now.AddMinutes(120),
            eventId: 4002);
        AllocationCandidate faraway = Candidate(
            Terrestrial27,
            priority: 30,
            from: Now.AddHours(5),
            to: Now.AddHours(6),
            eventId: 4003);
        AllocationCandidate taken = Candidate(Terrestrial29, priority: 20, eventId: 4004);
        AllocationCandidate lost = Candidate(Terrestrial31, priority: 10, eventId: 4005);

        AllocationPlan plan = Planned(
            [lost, taken, faraway, after, before],
            Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Contended, Verdict(plan, lost));
        Assert.Equal([taken.Id], plan.For(lost.Id).Instead);
    }

    [Fact]
    public void WhatIsBeingRecordedIsNamedAmongWhatWasRecordedInstead()
    {
        AllocationCandidate recording = Candidate(Terrestrial27, priority: 1, pinned: true, eventId: 4001);
        AllocationCandidate lost = Candidate(Terrestrial29, priority: 99, eventId: 4002);

        AllocationPlan plan = Planned([recording, lost], Capacity(TunerKind.Terrestrial));

        Assert.Equal([recording.Id], plan.For(lost.Id).Instead);
    }

    [Fact]
    public void WhatWasRecordedInsteadIsNamedByRankNotByWhenItTookItsSeat()
    {
        AllocationCandidate recording = Candidate(Terrestrial27, priority: 1, pinned: true, eventId: 4001);
        AllocationCandidate wanted = Candidate(Terrestrial29, priority: 30, eventId: 4002);
        AllocationCandidate lost = Candidate(Terrestrial31, priority: 10, eventId: 4003);

        AllocationPlan plan = Planned(
            [recording, wanted, lost],
            Capacity(TunerKind.Terrestrial, TunerKind.Terrestrial));

        Assert.Equal([wanted.Id, recording.Id], plan.For(lost.Id).Instead);
    }

    [Fact]
    public void AContendedCandidateHoldsNothingForTheOnesBehindIt()
    {
        AllocationCandidate taken = Candidate(Terrestrial27, priority: 30, eventId: 4001);
        AllocationCandidate lost = Candidate(Terrestrial29, priority: 20, eventId: 4002);
        AllocationCandidate rider = Candidate(Terrestrial27, priority: 10, eventId: 4003);

        AllocationPlan plan = Planned([taken, lost, rider], Capacity(TunerKind.Terrestrial));

        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, taken));
        Assert.Equal(AllocationVerdict.Contended, Verdict(plan, lost));
        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, rider));
    }

    [Fact]
    public void OnlyACandidateThatLostAContestNamesWhatWasRecordedInstead()
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(() => new AllocationDecision(
            ReservationId.New(),
            AllocationVerdict.Secured,
            [ReservationId.New()]));

        Assert.Equal("instead", refused.ParamName);
    }

    [Fact]
    public void ADecisionNamesAVerdictThePlannerReaches()
    {
        ArgumentOutOfRangeException refused = Assert.Throws<ArgumentOutOfRangeException>(() => new AllocationDecision(
            ReservationId.New(),
            (AllocationVerdict)9,
            []));

        Assert.Equal("verdict", refused.ParamName);
    }

    [Fact]
    public void ThePlanMovesTheReservationsItWasBuiltFrom()
    {
        Reservation kept = ReservationFactory.Planned(
            priority: new Priority(20),
            programme: ReservationFactory.Programme(4001));
        Reservation lost = ReservationFactory.Planned(
            priority: new Priority(10),
            programme: ReservationFactory.Programme(4002));

        AllocationPlan plan = Planned(
            [AllocationCandidate.Of(kept, Terrestrial27), AllocationCandidate.Of(lost, Terrestrial29)],
            Capacity(TunerKind.Terrestrial));

        Apply(plan, [kept, lost]);

        Assert.Equal(ReservationState.Scheduled, kept.State);
        Assert.Equal(ReservationState.Conflict, lost.State);
    }

    [Fact]
    public void APlanThatContendedARecordingCouldNotBeApplied()
    {
        Reservation recording = ReservationFactory.Claimed();
        Reservation wanted = ReservationFactory.Planned(programme: ReservationFactory.Programme(4002));

        AllocationPlan plan = Planned(
            [AllocationCandidate.Of(recording, Terrestrial27), AllocationCandidate.Of(wanted, Terrestrial29)],
            Capacity(TunerKind.Terrestrial));

        Apply(plan, [recording, wanted]);

        Assert.Equal(AllocationVerdict.Pinned, plan.For(recording.Id).Verdict);
        Assert.Equal(ReservationState.Scheduled, recording.State);
        Assert.Equal(ReservationState.Conflict, wanted.State);
        Assert.Throws<InvalidOperationException>(recording.Contend);
    }

    [Fact]
    public void AServiceWithNowhereToTuneIsMarkedRatherThanContended()
    {
        Reservation nowhere = ReservationFactory.Planned(programme: ReservationFactory.Programme(4001));

        AllocationPlan plan = Planned(
            [AllocationCandidate.Of(nowhere, null)],
            Capacity(TunerKind.Terrestrial));

        Apply(plan, [nowhere]);

        Assert.Equal(AllocationVerdict.Unreachable, plan.For(nowhere.Id).Verdict);
        Assert.Equal(ReservationState.Scheduled, nowhere.State);
        Assert.True(nowhere.ReceptionUnavailable);
        Assert.Equal(Now, nowhere.ReceptionUnavailableSince);
    }

    [Fact]
    public void AReservationThatCanBeTunedAgainStopsBeingMarked()
    {
        Reservation regained = ReservationFactory.Planned(programme: ReservationFactory.Programme(4001));
        regained.LoseReception(Now.AddHours(-1));

        AllocationPlan plan = Planned(
            [AllocationCandidate.Of(regained, Terrestrial27)],
            Capacity(TunerKind.Terrestrial));

        Apply(plan, [regained]);

        Assert.Equal(ReservationState.Scheduled, regained.State);
        Assert.False(regained.ReceptionUnavailable);
        Assert.Null(regained.ReceptionUnavailableSince);
    }

    [Fact]
    public void WhatWasRecordedInsteadIsWhatTheLedgerHolds()
    {
        Reservation kept = ReservationFactory.Planned(
            priority: new Priority(20),
            programme: ReservationFactory.Programme(4001));
        Reservation lost = ReservationFactory.Planned(
            priority: new Priority(10),
            programme: ReservationFactory.Programme(4002));

        AllocationPlan plan = Planned(
            [AllocationCandidate.Of(kept, Terrestrial27), AllocationCandidate.Of(lost, Terrestrial29)],
            Capacity(TunerKind.Terrestrial));

        ReservationOutcome recorded = ReservationOutcome.Record(
            ReservationOutcomeId.New(),
            lost,
            ReservationOutcomeKind.Competing,
            null,
            null,
            [.. plan.For(lost.Id).Instead.Select(id => id.Value)],
            Now);

        Assert.Equal([kept.Id.Value], recorded.RecordedInstead);
        Assert.Empty(plan.For(kept.Id).Instead);
    }

    private static void Apply(AllocationPlan plan, IReadOnlyList<Reservation> reservations)
    {
        foreach (Reservation reservation in reservations)
        {
            switch (plan.For(reservation.Id).Verdict)
            {
                case AllocationVerdict.Secured:
                case AllocationVerdict.Pinned:
                    reservation.RegainReception();
                    reservation.Secure();
                    break;

                case AllocationVerdict.Contended:
                    reservation.RegainReception();
                    reservation.Contend();
                    break;

                case AllocationVerdict.Unreachable:
                    reservation.LoseReception(Now);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"{plan.For(reservation.Id).Verdict} has no move on a reservation yet.");
            }
        }
    }

    private static AllocationPlan Planned(IReadOnlyList<AllocationCandidate> candidates, TunerCapacity capacity)
        => TunerAllocationPlanner.Plan(candidates, capacity, RollingHorizon.Default, Now);

    private static AllocationVerdict Verdict(AllocationPlan plan, AllocationCandidate candidate)
        => plan.For(candidate.Id).Verdict;

    private static string Describe(AllocationPlan plan)
        => string.Join(";", plan.Decisions.Select(decision => $"{decision.Id}={decision.Verdict}"));

    private static TunerCapacity Capacity(params TunerKind[] kinds)
        => new(
            [.. kinds.Select((kind, index) => new TunerSeat($"seat{index}", BroadcastReception.Of(kind), Faulted: false))],
            []);

    private static ProgrammeRef Programme(int networkId, int serviceId, int eventId, int startsAtOffsetMinutes)
        => new(
            new NetworkId(networkId),
            new ServiceId(serviceId),
            new EventId(eventId),
            Now.AddHours(2).AddMinutes(startsAtOffsetMinutes));

    private static AllocationCandidate Unfinished(bool pinned)
        => Candidate(
            Terrestrial27,
            from: Now.AddMinutes(-30),
            to: Now.AddMinutes(1),
            endAtConfirmed: false,
            pinned: pinned,
            id: new ReservationId(Guid.Parse("00000000-0000-0000-0000-0000000000a1")),
            eventId: 4001);

    private static AllocationCandidate Wanting()
        => Candidate(
            Terrestrial29,
            from: Now.AddMinutes(10),
            to: Now.AddMinutes(70),
            id: new ReservationId(Guid.Parse("00000000-0000-0000-0000-0000000000a2")),
            eventId: 4002);

    private static AllocationCandidate Candidate(
        TuningParameters? tuning,
        int priority = Priority.DefaultValue,
        int fromMinutes = 0,
        int forMinutes = 60,
        DateTime? from = null,
        DateTime? to = null,
        bool endAtConfirmed = true,
        bool pinned = false,
        ReservationId? id = null,
        ProgrammeRef? programme = null,
        int eventId = 4001)
    {
        DateTime opens = from ?? Now.AddMinutes(fromMinutes);

        return new AllocationCandidate(
            id ?? ReservationId.New(),
            programme ?? Programme(32736, 1024, eventId, 0),
            new Priority(priority),
            tuning,
            opens,
            to ?? opens.AddMinutes(forMinutes),
            endAtConfirmed,
            pinned);
    }
}
