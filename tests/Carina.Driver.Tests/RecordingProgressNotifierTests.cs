using Carina.Driver.Sessions;

namespace Carina.Driver.Tests;

public sealed class RecordingProgressNotifierTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 13, 21, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private static readonly Action<Exception> Ignored = _ => { };

    [Fact]
    public void ADriverWithNothingToRecordSaysNothingHoweverLongItRuns()
    {
        var clock = new SteppedTimeProvider(Start);
        var told = new List<int>();
        using var notifier = new RecordingProgressNotifier(
            () => false,
            () => told.Add(told.Count),
            clock,
            Ignored,
            Interval
        );

        clock.Advance(TimeSpan.FromHours(2));

        Assert.Empty(told);
        Assert.Equal(0, notifier.Notices);
    }

    [Fact]
    public void ARecordingInFlightIsSpokenForOnceTheIntervalHasPassed()
    {
        var clock = new SteppedTimeProvider(Start);
        var told = new List<int>();
        using var notifier = new RecordingProgressNotifier(
            () => true,
            () => told.Add(told.Count),
            clock,
            Ignored,
            Interval
        );

        Assert.Empty(told);

        clock.Advance(Interval);

        Assert.Single(told);
        Assert.Equal(1, notifier.Notices);
    }

    [Fact]
    public void TheCountIsSentAgainAtEveryIntervalRatherThanOnlyWhenTheRecordingEnds()
    {
        var clock = new SteppedTimeProvider(Start);
        RecordingProgressNotifier notifier = Counting(clock, out List<int> told);

        using (notifier)
        {
            for (int passed = 0; passed < 5; passed++)
            {
                clock.Advance(Interval);
            }
        }

        Assert.Equal(5, told.Count);
    }

    [Fact]
    public void TheIntervalThisDriverKeepsIsThirtySeconds()
    {
        var clock = new SteppedTimeProvider(Start);
        var told = new List<int>();
        using var notifier = new RecordingProgressNotifier(
            () => true,
            () => told.Add(told.Count),
            clock,
            Ignored
        );

        clock.Advance(TimeSpan.FromSeconds(29));

        Assert.Empty(told);

        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Single(told);
    }

    [Fact]
    public void ADriverThatHasNothingToRecordAgainGoesQuiet()
    {
        var clock = new SteppedTimeProvider(Start);
        var told = new List<int>();
        bool recording = true;
        using var notifier = new RecordingProgressNotifier(
            () => recording,
            () => told.Add(told.Count),
            clock,
            Ignored,
            Interval
        );

        clock.Advance(Interval);
        recording = false;
        clock.Advance(Interval);
        clock.Advance(Interval);

        Assert.Single(told);
    }

    [Fact]
    public void NothingIsSaidAfterTheDriverHasLetGoOfTheClock()
    {
        var clock = new SteppedTimeProvider(Start);
        RecordingProgressNotifier notifier = Counting(clock, out List<int> told);

        clock.Advance(Interval);
        notifier.Dispose();
        clock.Advance(Interval);
        clock.Advance(Interval);

        Assert.Single(told);
    }

    private static RecordingProgressNotifier Counting(
        SteppedTimeProvider clock,
        out List<int> told
    )
    {
        var spoken = new List<int>();

        told = spoken;

        return new RecordingProgressNotifier(
            () => true,
            () => spoken.Add(spoken.Count),
            clock,
            Ignored,
            Interval
        );
    }

    [Fact]
    public void AnAnnouncementThatThrowsDoesNotBringTheTimerThreadDown()
    {
        var clock = new SteppedTimeProvider(Start);
        var met = new List<Exception>();
        using var notifier = new RecordingProgressNotifier(
            () => true,
            () => throw new InvalidOperationException("the hub is gone"),
            clock,
            met.Add,
            Interval
        );

        clock.Advance(Interval);
        clock.Advance(Interval);

        Assert.Equal(2, notifier.Faults);
        Assert.Equal(0, notifier.Notices);
        Assert.Equal(2, met.Count);
        Assert.All(met, error => Assert.IsType<InvalidOperationException>(error));
    }

    [Fact]
    public void AskingWhetherAnythingIsRecordingIsAllowedToThrowToo()
    {
        var clock = new SteppedTimeProvider(Start);
        var met = new List<Exception>();
        using var notifier = new RecordingProgressNotifier(
            () => throw new InvalidOperationException("the sessions are gone"),
            () => { },
            clock,
            met.Add,
            Interval
        );

        clock.Advance(Interval);

        Assert.Equal(1, notifier.Faults);
        Assert.Equal(0, notifier.Notices);
    }
}
