using Carina.Domain.Channels;

namespace Carina.Domain.Tests.Channels;

public sealed class LogoRotationTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    private static readonly LogoSweepSettings Settings = new();

    [Fact]
    public void ATransportNobodyHasVisitedIsTheOneToOpen()
    {
        BroadcastStream due = Terrestrial(27, 1);

        Assert.Same(due, LogoRotation.NextDue([due], [], Settings, Now));
    }

    [Fact]
    public void ATransportVisitedLongestAgoGoesBeforeOneVisitedRecently()
    {
        BroadcastStream first = Terrestrial(27, 1);
        BroadcastStream second = Terrestrial(28, 2);

        BroadcastStream? due = LogoRotation.NextDue(
            [first, second],
            [
                Visited(1, LogoVisitOutcome.NothingArrived, Now.AddDays(-2)),
                Visited(2, LogoVisitOutcome.NothingArrived, Now.AddDays(-3)),
            ],
            Settings,
            Now);

        Assert.Same(second, due);
    }

    [Fact]
    public void ATransportWhoseLogosAreInHandIsLeftAloneUntilTheyAreOldEnoughToDoubt()
    {
        BroadcastStream held = Terrestrial(27, 1);
        LogoVisit collected = Visited(1, LogoVisitOutcome.Collected, Now.AddDays(-29));

        Assert.Null(LogoRotation.NextDue([held], [collected], Settings, Now));
        Assert.Same(held, LogoRotation.NextDue([held], [collected], Settings, Now.AddDays(2)));
    }

    [Fact]
    public void ATransportThatGaveNothingIsAskedAgainInHoursRatherThanInAMonth()
    {
        BroadcastStream empty = Terrestrial(27, 1);
        LogoVisit nothing = Visited(1, LogoVisitOutcome.NothingArrived, Now.AddHours(-5));

        Assert.Null(LogoRotation.NextDue([empty], [nothing], Settings, Now));
        Assert.Same(empty, LogoRotation.NextDue([empty], [nothing], Settings, Now.AddHours(2)));
    }

    [Fact]
    public void AVisitCutShortIsCarriedOverToTheNextSweepRatherThanWaitedOut()
    {
        BroadcastStream cut = Terrestrial(27, 1);

        Assert.Same(
            cut,
            LogoRotation.NextDue(
                [cut],
                [Visited(1, LogoVisitOutcome.Interrupted, Now)],
                Settings,
                Now));
    }

    [Fact]
    public void ATransportOnASatelliteIsNotOpenedBecauseItsLogosDoNotComeThisWay()
    {
        BroadcastStream satellite = new(
            new NetworkId(4),
            new TransportStreamId(16625),
            TuningParameters.Bs(1, new TransportStreamId(16625)),
            [new ServiceId(101)]);

        Assert.Null(LogoRotation.NextDue([satellite], [], Settings, Now));
        Assert.False(LogoRotation.CarriesACommonDataTable(satellite));
    }

    [Fact]
    public void OneSweepPicksOneTransportSoTheNextSweepCanPickAnother()
    {
        BroadcastStream first = Terrestrial(27, 1);
        BroadcastStream second = Terrestrial(28, 2);

        BroadcastStream? opened = LogoRotation.NextDue([first, second], [], Settings, Now);
        BroadcastStream? next = LogoRotation.NextDue(
            [first, second],
            [Visited(1, LogoVisitOutcome.Collected, Now)],
            Settings,
            Now);

        Assert.Same(first, opened);
        Assert.Same(second, next);
    }

    private static BroadcastStream Terrestrial(int physicalChannel, int transportStreamId)
        => new(
            new NetworkId(32736),
            new TransportStreamId(transportStreamId),
            TuningParameters.Terrestrial(physicalChannel),
            [new ServiceId(1024)]);

    private static LogoVisit Visited(int transportStreamId, LogoVisitOutcome outcome, DateTime at)
        => LogoVisit.Record(new NetworkId(32736), new TransportStreamId(transportStreamId), outcome, at);
}
