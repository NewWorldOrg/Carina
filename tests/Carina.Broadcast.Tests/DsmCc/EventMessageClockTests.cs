using Carina.Broadcast.DsmCc;
using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.DsmCc;

public sealed class EventMessageClockTests
{
    private const int SomeGroup = 1;

    private const long ReceivedAt = 900_000;

    [Fact]
    public void BR_BD_003_AnImmediateEventFiresAtThePtsItWasReceivedAt()
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
        Assert.Equal(GeneralEvent.Immediate, fired.TimeMode);
        Assert.Equal([0x55], fired.PrivateData.ToArray());
    }

    [Fact]
    public void BR_BD_003_AnImmediateEventReceivedPastTheWrapIsKeptInsideThirtyThreeBits()
    {
        var clock = new EventMessageClock();

        IReadOnlyList<EventMessageOutcome> outcomes = clock.Push(
            Section(0, StreamDescriptorWriter.GeneralEvent(SomeGroup, StreamDescriptorWriter.Immediate, 0, 0, 1)),
            EventMessageClock.PtsModulus + 5);

        Assert.Equal(5, Fired(outcomes).Single().FiresAt);
    }

    [Fact]
    public void BR_BD_003_AnNptEventIsTurnedIntoThePtsTheReferencePairsItWith()
    {
        var clock = new EventMessageClock();

        IReadOnlyList<EventMessageOutcome> outcomes = clock.Push(
            Section(
                0,
                StreamDescriptorWriter.NptReference(stc: 1_000_000, npt: 0),
                StreamDescriptorWriter.GeneralEvent(SomeGroup, StreamDescriptorWriter.Npt, 270_000, 0, 2)),
            ReceivedAt);

        Assert.Equal(1_270_000, Fired(outcomes).Single().FiresAt);
    }

    [Fact]
    public void BR_BD_003_TheNptScaleIsTheRateNptRunsAtAgainstTheSystemClock()
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

    [Fact]
    public void BR_BD_003_AnNptEventThatLandsPastTheWrapIsKeptInsideThirtyThreeBits()
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

    [Fact]
    public void BR_BD_003_AnNptEventWaitsForItsReferenceAndFiresWhenTheReferenceArrives()
    {
        var clock = new EventMessageClock();

        IReadOnlyList<EventMessageOutcome> before = clock.Push(
            Section(0, StreamDescriptorWriter.GeneralEvent(SomeGroup, StreamDescriptorWriter.Npt, 90_000, 0, 1)),
            ReceivedAt);
        IReadOnlyList<EventMessageOutcome> second = clock.Push(
            Section(1, StreamDescriptorWriter.GeneralEvent(SomeGroup, StreamDescriptorWriter.Npt, 180_000, 0, 2)),
            ReceivedAt);
        IReadOnlyList<EventMessageOutcome> after = clock.Push(
            new StreamDescriptorWriter { EventMessageGroupId = 0x0FFF, Descriptors = StreamDescriptorWriter.NptReference(stc: 5_000_000, npt: 0) },
            ReceivedAt);

        Assert.Empty(before);
        Assert.Empty(second);
        Assert.Equal([1, 2], Fired(after).Select(fired => fired.EventMessageId));
        Assert.Equal([5_090_000L, 5_180_000L], Fired(after).Select(fired => fired.FiresAt));
    }

    [Fact]
    public void BR_BD_003_TheNewestReferenceIsTheOneLaterEventsAreTurnedWith()
    {
        var clock = new EventMessageClock();
        clock.Push(new StreamDescriptorWriter { Descriptors = StreamDescriptorWriter.NptReference(stc: 1_000, npt: 0) }, ReceivedAt);
        clock.Push(new StreamDescriptorWriter { Descriptors = StreamDescriptorWriter.NptReference(stc: 2_000, npt: 0) }, ReceivedAt);

        IReadOnlyList<EventMessageOutcome> outcomes = clock.Push(
            Section(3, StreamDescriptorWriter.GeneralEvent(SomeGroup, StreamDescriptorWriter.Npt, 10, 0, 1)),
            ReceivedAt);

        Assert.Equal(2_010, Fired(outcomes).Single().FiresAt);
    }

    [Fact]
    public void BR_BD_003_ARepeatOfTheSameSectionVersionFiresItsEventsOnceAndANewVersionFiresAgain()
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

    [Fact]
    public void BR_BD_003_ATimeModeOtherThanImmediateOrNptIsDiscarded()
    {
        var clock = new EventMessageClock();

        IReadOnlyList<EventMessageOutcome> outcomes = clock.Push(
            Section(0, StreamDescriptorWriter.GeneralEvent(SomeGroup, StreamDescriptorWriter.MjdJst, 0xE4_5A_12_00_00, 0, 1)),
            ReceivedAt);

        Assert.Equal(EventMessageDefect.UnsupportedTimeMode, Assert.IsType<EventMessageOutcome.Discarded>(outcomes.Single()).Defect);
    }

    [Fact]
    public void BR_BD_003_AReferenceWithAZeroScaleIsDiscardedAndTheEventKeepsWaiting()
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

    [Fact]
    public void BR_BV_001_EventsWaitingPastTheLimitForAReferenceAreDiscarded()
    {
        var clock = new EventMessageClock();
        int discarded = 0;

        for (int version = 0; version <= EventMessageClock.MostWaiting; version++)
        {
            discarded += clock.Push(
                    new StreamDescriptorWriter
                    {
                        EventMessageGroupId = version >> 5,
                        VersionNumber = version & 0x1F,
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
