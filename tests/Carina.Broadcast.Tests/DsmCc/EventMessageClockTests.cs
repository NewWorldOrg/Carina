using Carina.Broadcast.DsmCc;
using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.DsmCc;

public sealed class EventMessageClockTests
{
    private const int SomeGroup = 1;

    private const long ReceivedAt = 900_000;

    [Fact(DisplayName = "BR-BD-003: an immediate event fires at the PTS it was received at")]
    public void AnImmediateEventFiresAtThePtsItWasReceivedAt()
    {
        var clock = new EventMessageClock();

        IReadOnlyList<EventMessageOutcome> outcomes = clock.Push(
            Section(0, StreamDescriptorWriter.GeneralEvent(SomeGroup, StreamDescriptorWriter.Immediate, 0, 0x01, 0x0010, 0x55)),
            ReceivedAt);

        TimedEventMessage fired = Fired(outcomes).Single();
        Assert.Equal(ReceivedAt, fired.FiresAt);
        Assert.Equal(SomeGroup, fired.EventMessageGroupId);
        Assert.Equal(0x0010, fired.EventMessageId);
        Assert.Equal(0x01, fired.EventMessageType);
        Assert.Equal(EventTimeMode.Immediate, fired.TimeMode);
        Assert.True(fired.IsImmediate);
        Assert.Equal([0x55], fired.PrivateData.ToArray());
    }

    [Fact(DisplayName = "BR-BD-003: an immediate event received past the wrap is kept inside thirty-three bits")]
    public void AnImmediateEventReceivedPastTheWrapIsKeptInsideThirtyThreeBits()
    {
        var clock = new EventMessageClock();

        IReadOnlyList<EventMessageOutcome> outcomes = clock.Push(
            Section(0, StreamDescriptorWriter.GeneralEvent(SomeGroup, StreamDescriptorWriter.Immediate, 0, 0, 1)),
            EventMessageClock.PtsModulus + 5);

        Assert.Equal(5, Fired(outcomes).Single().FiresAt);
    }

    [Fact(DisplayName = "BR-BD-003: an NPT event is turned into the PTS the reference pairs it with")]
    public void AnNptEventIsTurnedIntoThePtsTheReferencePairsItWith()
    {
        var clock = new EventMessageClock();

        IReadOnlyList<EventMessageOutcome> outcomes = clock.Push(
            Section(
                0,
                StreamDescriptorWriter.NptReference(stc: 1_000_000, npt: 0),
                StreamDescriptorWriter.GeneralEvent(SomeGroup, StreamDescriptorWriter.Npt, 270_000, 0, 2)),
            ReceivedAt);

        Assert.Equal(1_270_000, Fired(outcomes).Single().FiresAt);
        Assert.Equal(EventTimeMode.Npt, Fired(outcomes).Single().TimeMode);
        Assert.False(Fired(outcomes).Single().IsImmediate);
    }

    [Fact(DisplayName = "BR-BD-003: the NPT scale is the rate NPT runs at against the system clock")]
    public void TheNptScaleIsTheRateNptRunsAtAgainstTheSystemClock()
    {
        var clock = new EventMessageClock();

        IReadOnlyList<EventMessageOutcome> outcomes = clock.Push(
            Section(
                0,
                StreamDescriptorWriter.NptReference(stc: 1_000_000, npt: 90_000, scaleNumerator: 2, scaleDenominator: 1),
                StreamDescriptorWriter.GeneralEvent(SomeGroup, StreamDescriptorWriter.Npt, 270_000, 0, 2)),
            ReceivedAt);

        Assert.Equal(1_090_000, Fired(outcomes).Single().FiresAt);
    }

    [Fact(DisplayName = "BR-BD-003: an NPT event that lands past the wrap is kept inside thirty-three bits")]
    public void AnNptEventThatLandsPastTheWrapIsKeptInsideThirtyThreeBits()
    {
        var clock = new EventMessageClock();

        IReadOnlyList<EventMessageOutcome> outcomes = clock.Push(
            Section(
                0,
                StreamDescriptorWriter.NptReference(stc: EventMessageClock.PtsModulus - 10, npt: 0),
                StreamDescriptorWriter.GeneralEvent(SomeGroup, StreamDescriptorWriter.Npt, 30, 0, 2)),
            ReceivedAt);

        Assert.Equal(20, Fired(outcomes).Single().FiresAt);
    }

    [Fact(DisplayName = "BR-BD-003: an NPT event waits for its reference and fires when the reference arrives")]
    public void AnNptEventWaitsForItsReferenceAndFiresWhenTheReferenceArrives()
    {
        var clock = new EventMessageClock();

        IReadOnlyList<EventMessageOutcome> before = clock.Push(
            Section(0, StreamDescriptorWriter.GeneralEvent(SomeGroup, StreamDescriptorWriter.Npt, 90_000, 0, 1)),
            ReceivedAt);
        IReadOnlyList<EventMessageOutcome> second = clock.Push(
            new StreamDescriptorWriter
            {
                EventMessageGroupId = SomeGroup + 1,
                Descriptors = StreamDescriptorWriter.GeneralEvent(SomeGroup, StreamDescriptorWriter.Npt, 180_000, 0, 2),
            },
            ReceivedAt);
        IReadOnlyList<EventMessageOutcome> after = clock.Push(
            new StreamDescriptorWriter { EventMessageGroupId = 0x0FFF, Descriptors = StreamDescriptorWriter.NptReference(stc: 5_000_000, npt: 0) },
            ReceivedAt);

        Assert.Empty(before);
        Assert.Empty(second);
        Assert.Equal([1, 2], Fired(after).Select(fired => fired.EventMessageId));
        Assert.Equal([5_090_000L, 5_180_000L], Fired(after).Select(fired => fired.FiresAt));
    }

    [Fact(DisplayName = "BR-BD-003: the newest reference is the one later events are turned with")]
    public void TheNewestReferenceIsTheOneLaterEventsAreTurnedWith()
    {
        var clock = new EventMessageClock();
        clock.Push(new StreamDescriptorWriter { Descriptors = StreamDescriptorWriter.NptReference(stc: 1_000, npt: 0) }, ReceivedAt);
        clock.Push(new StreamDescriptorWriter { VersionNumber = 1, Descriptors = StreamDescriptorWriter.NptReference(stc: 2_000, npt: 0) }, ReceivedAt);

        IReadOnlyList<EventMessageOutcome> outcomes = clock.Push(
            Section(3, StreamDescriptorWriter.GeneralEvent(SomeGroup, StreamDescriptorWriter.Npt, 10, 0, 1)),
            ReceivedAt);

        Assert.Equal(2_010, Fired(outcomes).Single().FiresAt);
    }

    [Fact(DisplayName = "BR-BD-003: a repeat of the same section version fires its events once and a new version fires again")]
    public void ARepeatOfTheSameSectionVersionFiresItsEventsOnceAndANewVersionFiresAgain()
    {
        var clock = new EventMessageClock();
        byte[] immediate = StreamDescriptorWriter.GeneralEvent(SomeGroup, StreamDescriptorWriter.Immediate, 0, 0, 1);

        IReadOnlyList<EventMessageOutcome> first = clock.Push(Section(4, immediate), ReceivedAt);
        IReadOnlyList<EventMessageOutcome> repeat = clock.Push(Section(4, immediate), ReceivedAt + 90_000);
        IReadOnlyList<EventMessageOutcome> next = clock.Push(Section(5, immediate), ReceivedAt + 180_000);

        Assert.Single(Fired(first));
        Assert.Empty(repeat);
        Assert.Equal(ReceivedAt + 180_000, Fired(next).Single().FiresAt);
    }

    [Fact(DisplayName = "BR-BD-003: each section of a version carries events of its own and all of them fire")]
    public void EachSectionOfAVersionCarriesEventsOfItsOwnAndAllOfThemFire()
    {
        var clock = new EventMessageClock();

        IReadOnlyList<TimedEventMessage> fired = Enumerable.Range(0, 2)
            .SelectMany(number => clock.Push(
                new StreamDescriptorWriter
                {
                    EventMessageGroupId = SomeGroup,
                    VersionNumber = 3,
                    SectionNumber = number,
                    LastSectionNumber = 1,
                    Descriptors = StreamDescriptorWriter.GeneralEvent(SomeGroup, StreamDescriptorWriter.Immediate, 0, 0, 10 + number),
                },
                ReceivedAt))
            .OfType<EventMessageOutcome.Timed>()
            .Select(timed => timed.Message)
            .ToArray();

        Assert.Equal([10, 11], fired.Select(message => message.EventMessageId));
    }

    [Fact(DisplayName = "BR-BD-003: a section that is not yet current fires nothing and moves no reference")]
    public void ASectionThatIsNotYetCurrentFiresNothingAndMovesNoReference()
    {
        var clock = new EventMessageClock();
        clock.Push(new StreamDescriptorWriter { Descriptors = StreamDescriptorWriter.NptReference(stc: 1_000, npt: 0) }, ReceivedAt);

        IReadOnlyList<EventMessageOutcome> next = clock.Push(
            new StreamDescriptorWriter
            {
                EventMessageGroupId = SomeGroup,
                IsCurrent = false,
                Descriptors = DescriptorWriter.Loop(
                    StreamDescriptorWriter.NptReference(stc: 9_000, npt: 0),
                    StreamDescriptorWriter.GeneralEvent(SomeGroup, StreamDescriptorWriter.Immediate, 0, 0, 1)),
            },
            ReceivedAt);
        IReadOnlyList<EventMessageOutcome> current = clock.Push(
            Section(0, StreamDescriptorWriter.GeneralEvent(SomeGroup, StreamDescriptorWriter.Npt, 10, 0, 2)),
            ReceivedAt);

        Assert.Empty(next);
        Assert.Equal(1_010, Fired(current).Single().FiresAt);
    }

    [Fact(DisplayName = "BR-BD-003: a new version of a section discards the events of the old one still waiting for a reference")]
    public void ANewVersionOfASectionDiscardsTheEventsOfTheOldOneStillWaitingForAReference()
    {
        var clock = new EventMessageClock();
        clock.Push(Section(0, StreamDescriptorWriter.GeneralEvent(SomeGroup, StreamDescriptorWriter.Npt, 10, 0, 1)), ReceivedAt);

        IReadOnlyList<EventMessageOutcome> replaced = clock.Push(
            Section(1, StreamDescriptorWriter.GeneralEvent(SomeGroup, StreamDescriptorWriter.Npt, 20, 0, 2)),
            ReceivedAt);
        IReadOnlyList<EventMessageOutcome> released = clock.Push(
            new StreamDescriptorWriter { EventMessageGroupId = 0x0FFF, Descriptors = StreamDescriptorWriter.NptReference(stc: 1_000, npt: 0) },
            ReceivedAt);

        Assert.Equal(EventMessageDefect.Superseded, Assert.IsType<EventMessageOutcome.Discarded>(Assert.Single(replaced)).Defect);
        Assert.Equal([2], Fired(released).Select(message => message.EventMessageId));
    }

    [Fact(DisplayName = "BR-BD-003: an unusable reference repeated in the same version is reported once")]
    public void AnUnusableReferenceRepeatedInTheSameVersionIsReportedOnce()
    {
        var clock = new EventMessageClock();
        StreamDescriptorWriter unusable = new() { Descriptors = StreamDescriptorWriter.NptReference(stc: 1_000, npt: 0, scaleNumerator: 0) };

        IReadOnlyList<EventMessageOutcome> first = clock.Push(unusable, ReceivedAt);
        IReadOnlyList<EventMessageOutcome> repeat = clock.Push(unusable, ReceivedAt);

        Assert.Single(first);
        Assert.Empty(repeat);
    }

    [Fact(DisplayName = "BR-BD-003: a time mode other than immediate or NPT is discarded")]
    public void ATimeModeOtherThanImmediateOrNptIsDiscarded()
    {
        var clock = new EventMessageClock();

        IReadOnlyList<EventMessageOutcome> outcomes = clock.Push(
            Section(0, StreamDescriptorWriter.GeneralEvent(SomeGroup, StreamDescriptorWriter.MjdJst, 0xE4_5A_12_00_00, 0, 1)),
            ReceivedAt);

        Assert.Equal(EventMessageDefect.UnsupportedTimeMode, Assert.IsType<EventMessageOutcome.Discarded>(outcomes.Single()).Defect);
    }

    [Fact(DisplayName = "BR-BD-003: a reference with a zero scale is discarded and the event keeps waiting")]
    public void AReferenceWithAZeroScaleIsDiscardedAndTheEventKeepsWaiting()
    {
        var clock = new EventMessageClock();

        IReadOnlyList<EventMessageOutcome> outcomes = clock.Push(
            Section(
                0,
                StreamDescriptorWriter.NptReference(stc: 1_000, npt: 0, scaleNumerator: 0, scaleDenominator: 1),
                StreamDescriptorWriter.GeneralEvent(SomeGroup, StreamDescriptorWriter.Npt, 10, 0, 1)),
            ReceivedAt);
        IReadOnlyList<EventMessageOutcome> later = clock.Push(
            new StreamDescriptorWriter { Descriptors = StreamDescriptorWriter.NptReference(stc: 1_000, npt: 0) },
            ReceivedAt);

        Assert.Equal(EventMessageDefect.UnusableNptReference, Assert.IsType<EventMessageOutcome.Discarded>(outcomes.Single()).Defect);
        Assert.Equal(1_010, Fired(later).Single().FiresAt);
    }

    [Fact(DisplayName = "BR-BV-001: events waiting past the limit for a reference are discarded")]
    public void EventsWaitingPastTheLimitForAReferenceAreDiscarded()
    {
        var clock = new EventMessageClock();
        int discarded = 0;

        for (int version = 0; version <= EventMessageClock.MostWaiting; version++)
        {
            discarded += clock.Push(
                    new StreamDescriptorWriter
                    {
                        EventMessageGroupId = version,
                        Descriptors = StreamDescriptorWriter.GeneralEvent(SomeGroup, StreamDescriptorWriter.Npt, version, 0, version),
                    },
                    ReceivedAt)
                .OfType<EventMessageOutcome.Discarded>()
                .Count(outcome => outcome.Defect == EventMessageDefect.TooManyWaiting);
        }

        IReadOnlyList<EventMessageOutcome> released = clock.Push(
            new StreamDescriptorWriter { EventMessageGroupId = 0x0FFF, Descriptors = StreamDescriptorWriter.NptReference(stc: 0, npt: 0) },
            ReceivedAt);

        Assert.Equal(1, discarded);
        Assert.Equal(EventMessageClock.MostWaiting, Fired(released).Count);
    }

    [Fact(DisplayName = "BR-BD-003: a reset forgets the reference, the waiting events and the versions seen")]
    public void AResetForgetsTheReferenceTheWaitingEventsAndTheVersionsSeen()
    {
        var clock = new EventMessageClock();
        byte[] immediate = StreamDescriptorWriter.GeneralEvent(SomeGroup, StreamDescriptorWriter.Immediate, 0, 0, 1);

        for (int version = 0; version < EventMessageClock.MostWaiting; version++)
        {
            clock.Push(
                new StreamDescriptorWriter
                {
                    EventMessageGroupId = version,
                    Descriptors = StreamDescriptorWriter.GeneralEvent(SomeGroup, StreamDescriptorWriter.Npt, version, 0, version),
                },
                ReceivedAt);
        }

        clock.Push(Section(9, immediate), ReceivedAt);
        clock.Reset();

        IReadOnlyList<EventMessageOutcome> waiting = clock.Push(
            new StreamDescriptorWriter { EventMessageGroupId = 0x0FFE, Descriptors = StreamDescriptorWriter.GeneralEvent(SomeGroup, StreamDescriptorWriter.Npt, 5, 0, 7) },
            ReceivedAt);
        IReadOnlyList<EventMessageOutcome> again = clock.Push(Section(9, immediate), ReceivedAt);
        IReadOnlyList<EventMessageOutcome> released = clock.Push(
            new StreamDescriptorWriter { EventMessageGroupId = 0x0FFF, Descriptors = StreamDescriptorWriter.NptReference(stc: 100, npt: 0) },
            ReceivedAt);

        Assert.Empty(waiting);
        Assert.Single(Fired(again));
        Assert.Equal([7], Fired(released).Select(message => message.EventMessageId));
    }

    [Fact(DisplayName = "BR-BD-003: events released by a reference do not wait for the next one")]
    public void EventsReleasedByAReferenceDoNotWaitForTheNextOne()
    {
        var clock = new EventMessageClock();
        clock.Push(Section(0, StreamDescriptorWriter.GeneralEvent(SomeGroup, StreamDescriptorWriter.Npt, 5, 0, 1)), ReceivedAt);
        clock.Push(new StreamDescriptorWriter { EventMessageGroupId = 0x0FFF, Descriptors = StreamDescriptorWriter.NptReference(stc: 100, npt: 0) }, ReceivedAt);

        IReadOnlyList<EventMessageOutcome> second = clock.Push(
            new StreamDescriptorWriter { EventMessageGroupId = 0x0FFF, VersionNumber = 1, Descriptors = StreamDescriptorWriter.NptReference(stc: 200, npt: 0) },
            ReceivedAt);

        Assert.Empty(second);
    }

    private static StreamDescriptorWriter Section(int version, params byte[][] descriptors)
        => new() { EventMessageGroupId = SomeGroup, VersionNumber = version, Descriptors = DescriptorWriter.Loop(descriptors) };

    private static IReadOnlyList<TimedEventMessage> Fired(IReadOnlyList<EventMessageOutcome> outcomes)
        => outcomes.OfType<EventMessageOutcome.Timed>().Select(timed => timed.Message).ToArray();
}

internal static class EventMessageClockExtensions
{
    public static IReadOnlyList<EventMessageOutcome> Push(this EventMessageClock clock, StreamDescriptorWriter writer, long receivedPts)
        => clock.Push(
            Assert.IsType<TableRead<StreamDescriptorSection>.Parsed>(StreamDescriptorSection.Read(CarriedSection.Of(writer.ToSection()))).Table,
            receivedPts);
}
