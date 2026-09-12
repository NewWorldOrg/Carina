using Carina.Domain.Channels;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class LiveDepartureLedgerTests
{
    private static readonly DateTimeOffset Opened = new(2026, 9, 13, 4, 0, 0, TimeSpan.Zero);

    private static readonly LiveSessionKey Watched =
        new(new NetworkId(32736), new ServiceId(1024), LiveProfile.Hd30);

    [Fact]
    public void EveryWayAWireCanEndIsAnsweredForBeforeAnyWireHasEnded()
    {
        LiveDepartureTally tally = new LiveDepartureLedger(
            new HandTurnedClock(Opened),
            NullLogger<LiveDepartureLedger>.Instance).Read();

        Assert.Equal(Enum.GetValues<LiveDeparture>(), tally.Counted.Select(counted => counted.Departure));
        Assert.All(tally.Counted, counted => Assert.Equal(0L, counted.Times));
        Assert.All(tally.Counted, counted => Assert.Null(counted.LastAt));
        Assert.All(tally.Counted, counted => Assert.Null(counted.Shortest));
        Assert.All(tally.Counted, counted => Assert.Null(counted.Longest));
        Assert.Equal(Opened.UtcDateTime, tally.Since);
    }

    [Fact]
    public void AWireThatEndedIsCountedUnderTheWayItEndedAndNowhereElse()
    {
        LiveDepartureLedger ledger = new(new HandTurnedClock(Opened), NullLogger<LiveDepartureLedger>.Instance);

        ledger.Note(Watched, LiveDeparture.ViewerStoppedReading, TimeSpan.FromSeconds(4));

        Assert.Equal(1L, Counted(ledger, LiveDeparture.ViewerStoppedReading).Times);
        Assert.Equal(
            1L,
            ledger.Read().Counted.Sum(counted => counted.Times));
    }

    [Fact]
    public void TheSameWayTwiceIsCountedTwiceAndSaysWhenItLastHappened()
    {
        HandTurnedClock clock = new(Opened);
        LiveDepartureLedger ledger = new(clock, NullLogger<LiveDepartureLedger>.Instance);

        ledger.Note(Watched, LiveDeparture.ViewerLeft, TimeSpan.FromSeconds(1));
        clock.Turn(TimeSpan.FromMinutes(20));
        ledger.Note(Watched, LiveDeparture.ViewerLeft, TimeSpan.FromSeconds(1_200));

        LiveDepartureCount left = Counted(ledger, LiveDeparture.ViewerLeft);

        Assert.Equal(2L, left.Times);
        Assert.Equal(Opened.UtcDateTime.AddMinutes(20), left.LastAt);
    }

    [Fact]
    public void HowLongTheShortestAndTheLongestWireOfAWayLastedAreBothKept()
    {
        LiveDepartureLedger ledger = new(new HandTurnedClock(Opened), NullLogger<LiveDepartureLedger>.Instance);

        ledger.Note(Watched, LiveDeparture.ViewerStoppedReading, TimeSpan.FromMinutes(20));
        ledger.Note(Watched, LiveDeparture.ViewerStoppedReading, TimeSpan.FromSeconds(3));
        ledger.Note(Watched, LiveDeparture.ViewerStoppedReading, TimeSpan.FromMinutes(19));

        LiveDepartureCount counted = Counted(ledger, LiveDeparture.ViewerStoppedReading);

        Assert.Equal(TimeSpan.FromSeconds(3), counted.Shortest);
        Assert.Equal(TimeSpan.FromMinutes(20), counted.Longest);
    }

    [Fact]
    public void OneWireOfAWayIsBothTheShortestAndTheLongestOfIt()
    {
        LiveDepartureLedger ledger = new(new HandTurnedClock(Opened), NullLogger<LiveDepartureLedger>.Instance);

        ledger.Note(Watched, LiveDeparture.SourceWentQuiet, TimeSpan.FromMinutes(20));

        LiveDepartureCount counted = Counted(ledger, LiveDeparture.SourceWentQuiet);

        Assert.Equal(TimeSpan.FromMinutes(20), counted.Shortest);
        Assert.Equal(TimeSpan.FromMinutes(20), counted.Longest);
        Assert.Null(Counted(ledger, LiveDeparture.ViewerLeft).Shortest);
    }

    [Fact]
    public void HowLongAWireLastedIsKeptApartByTheWayItEnded()
    {
        LiveDepartureLedger ledger = new(new HandTurnedClock(Opened), NullLogger<LiveDepartureLedger>.Instance);

        ledger.Note(Watched, LiveDeparture.ViewerLeft, TimeSpan.FromSeconds(3));
        ledger.Note(Watched, LiveDeparture.SourceWentQuiet, TimeSpan.FromMinutes(20));

        Assert.Equal(TimeSpan.FromSeconds(3), Counted(ledger, LiveDeparture.ViewerLeft).Longest);
        Assert.Equal(TimeSpan.FromMinutes(20), Counted(ledger, LiveDeparture.SourceWentQuiet).Shortest);
    }

    [Fact]
    public void AWireThatLastedLessThanNoTimeIsRefusedRatherThanCounted()
    {
        LiveDepartureLedger ledger = new(new HandTurnedClock(Opened), NullLogger<LiveDepartureLedger>.Instance);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ledger.Note(Watched, LiveDeparture.ViewerLeft, TimeSpan.FromSeconds(-1)));
        Assert.Equal(0L, Counted(ledger, LiveDeparture.ViewerLeft).Times);
    }

    [Fact]
    public void EachWayIsKeptApartFromTheOthers()
    {
        LiveDepartureLedger ledger = new(new HandTurnedClock(Opened), NullLogger<LiveDepartureLedger>.Instance);

        ledger.Note(Watched, LiveDeparture.ViewerLeft, TimeSpan.FromSeconds(1));
        ledger.Note(Watched, LiveDeparture.SourceWentQuiet, TimeSpan.FromSeconds(2));
        ledger.Note(Watched, LiveDeparture.SourceWentQuiet, TimeSpan.FromSeconds(3));

        Assert.Equal(1L, Counted(ledger, LiveDeparture.ViewerLeft).Times);
        Assert.Equal(2L, Counted(ledger, LiveDeparture.SourceWentQuiet).Times);
        Assert.Equal(0L, Counted(ledger, LiveDeparture.ServerStopping).Times);
    }

    [Fact]
    public void WhenItStartedCountingDoesNotMoveAsWiresEnd()
    {
        HandTurnedClock clock = new(Opened);
        LiveDepartureLedger ledger = new(clock, NullLogger<LiveDepartureLedger>.Instance);

        clock.Turn(TimeSpan.FromHours(3));
        ledger.Note(Watched, LiveDeparture.ViewerLeft, TimeSpan.FromSeconds(1));

        Assert.Equal(Opened.UtcDateTime, ledger.Read().Since);
    }

    [Fact]
    public void AWayNothingNamesIsRefusedRatherThanCountedSomewhere()
    {
        LiveDepartureLedger ledger = new(new HandTurnedClock(Opened), NullLogger<LiveDepartureLedger>.Instance);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ledger.Note(Watched, (LiveDeparture)99, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void HowAWireEndedAndHowLongItLastedBothReachTheLog()
    {
        RecordedDepartures log = new();
        LiveDepartureLedger ledger = new(new HandTurnedClock(Opened), log);

        ledger.Note(Watched, LiveDeparture.ViewerStoppedReading, TimeSpan.FromSeconds(1_200));

        Assert.Single(log.Lines);
        Assert.Contains(nameof(LiveDeparture.ViewerStoppedReading), log.Lines[0], StringComparison.Ordinal);
        Assert.Contains("1200", log.Lines[0], StringComparison.Ordinal);
        Assert.Contains(Watched.ToString(), log.Lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void CountingFromManyWiresAtOnceLosesNothing()
    {
        LiveDepartureLedger ledger = new(new HandTurnedClock(Opened), NullLogger<LiveDepartureLedger>.Instance);

        Parallel.For(0, 2_000, at => ledger.Note(
            Watched,
            at % 2 is 0 ? LiveDeparture.ViewerLeft : LiveDeparture.ViewerStoppedReading,
            TimeSpan.FromSeconds(1)));

        Assert.Equal(1_000L, Counted(ledger, LiveDeparture.ViewerLeft).Times);
        Assert.Equal(1_000L, Counted(ledger, LiveDeparture.ViewerStoppedReading).Times);
    }

    private static LiveDepartureCount Counted(LiveDepartureLedger ledger, LiveDeparture departure)
        => ledger.Read().Counted.Single(counted => counted.Departure == departure);

    private sealed class RecordedDepartures : ILogger<LiveDepartureLedger>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            Lines.Add(formatter(state, exception));
        }
    }
}
