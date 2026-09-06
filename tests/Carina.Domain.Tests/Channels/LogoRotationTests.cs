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

        Assert.Same(due, Assert.Single(LogoRotation.DueNow([due], [], Settings, Now)));
    }

    [Fact]
    public void ATransportVisitedLongestAgoGoesBeforeOneVisitedRecently()
    {
        BroadcastStream first = Terrestrial(27, 1);
        BroadcastStream second = Terrestrial(28, 2);

        IReadOnlyList<BroadcastStream> due = LogoRotation.DueNow(
            [first, second],
            [
                Visited(1, LogoVisitOutcome.NothingArrived, Now.AddDays(-2)),
                Visited(2, LogoVisitOutcome.NothingArrived, Now.AddDays(-3)),
            ],
            Settings,
            Now);

        Assert.Equal([second, first], due);
    }

    [Fact]
    public void ATransportWhoseLogosAreInHandIsLeftAloneUntilTheyAreOldEnoughToDoubt()
    {
        BroadcastStream held = Terrestrial(27, 1);
        LogoVisit collected = Visited(1, LogoVisitOutcome.Collected, Now.AddDays(-29));

        Assert.Empty(LogoRotation.DueNow([held], [collected], Settings, Now));
        Assert.Same(held, Assert.Single(LogoRotation.DueNow([held], [collected], Settings, Now.AddDays(2))));
    }

    [Fact]
    public void ATransportThatGaveNothingIsAskedAgainInHoursRatherThanInAMonth()
    {
        BroadcastStream empty = Terrestrial(27, 1);
        LogoVisit nothing = Visited(1, LogoVisitOutcome.NothingArrived, Now.AddHours(-5));

        Assert.Empty(LogoRotation.DueNow([empty], [nothing], Settings, Now));
        Assert.Same(empty, Assert.Single(LogoRotation.DueNow([empty], [nothing], Settings, Now.AddHours(2))));
    }

    [Fact]
    public void AVisitCutShortIsCarriedOverToTheNextSweepRatherThanWaitedOut()
    {
        BroadcastStream cut = Terrestrial(27, 1);

        Assert.Same(
            cut,
            Assert.Single(LogoRotation.DueNow(
                [cut],
                [Visited(1, LogoVisitOutcome.Interrupted, Now)],
                Settings,
                Now)));
    }

    [Fact]
    public void ATransportOnASatelliteIsNotOpenedBecauseItsLogosDoNotComeThisWay()
    {
        BroadcastStream satellite = new(
            new NetworkId(4),
            new TransportStreamId(16625),
            TuningParameters.Bs(1, new TransportStreamId(16625)),
            [new ServiceId(101)]);

        Assert.Empty(LogoRotation.DueNow([satellite], [], Settings, Now));
        Assert.False(LogoRotation.CarriesACommonDataTable(satellite));
    }

    [Fact]
    public void OneWakeIsHandedEveryTransportThatIsDueRatherThanOnlyTheFirstOfThem()
    {
        BroadcastStream first = Terrestrial(27, 1);
        BroadcastStream second = Terrestrial(28, 2);
        BroadcastStream third = Terrestrial(29, 3);

        IReadOnlyList<BroadcastStream> due = LogoRotation.DueNow(
            [first, second, third],
            [Visited(2, LogoVisitOutcome.Collected, Now)],
            Settings,
            Now);

        Assert.Equal([first, third], due);
    }

    [Fact]
    public void AWakeOpensItsFirstTransportHoweverLittleOfTheCycleTheBudgetLeavesIt()
    {
        LogoSweepSettings narrow = new() { BetweenSweeps = TimeSpan.FromMinutes(11) };

        Assert.True(LogoRotation.ThereIsRoomForAnotherVisit(narrow, TimeSpan.Zero, 0));
        Assert.False(LogoRotation.ThereIsRoomForAnotherVisit(narrow, TimeSpan.Zero, 1));
    }

    [Fact]
    public void AWakeOpensAnotherTransportOnlyWhileAWholeVisitStillFitsInTheBudget()
    {
        Assert.True(LogoRotation.ThereIsRoomForAnotherVisit(Settings, TimeSpan.FromMinutes(40), 4));
        Assert.False(LogoRotation.ThereIsRoomForAnotherVisit(Settings, TimeSpan.FromMinutes(41), 4));
    }

    [Fact]
    public void AWakeGetsThroughFiveTransportsBeforeItsBudgetIsSpent()
    {
        TimeSpan spent = TimeSpan.Zero;
        int visited = 0;

        while (LogoRotation.ThereIsRoomForAnotherVisit(Settings, spent, visited))
        {
            visited++;
            spent += Settings.LongestVisit;
        }

        Assert.Equal(5, visited);
        Assert.True(spent <= Settings.RoundBudget);
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
