using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Recordings;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using static Carina.Infrastructure.Tests.Recordings.RecordingTickFixture;

namespace Carina.Infrastructure.Tests.Recordings;

public sealed class RecordingTickJobTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task TheLoopTicksOnceItsFirstWaitIsOverAndKeepsTickingAfterThat()
    {
        var clock = new HurriedTicks();
        var recordings = new HeldRecordings();
        var driver = new RecordingDriver();
        using RecordingTickJob job = Job(
            Holding(Due(1)),
            recordings,
            driver,
            clock,
            new RecordingSettings(
                TimeSpan.FromMinutes(3),
                TimeSpan.FromHours(2),
                TimeSpan.FromHours(3),
                new OutputRoot("primary")));
        using var stopping = new CancellationTokenSource();

        await job.StartAsync(stopping.Token);
        await Eventually.Happens(() => driver.Started.Count >= 2, "the loop never ticked twice");
        await stopping.CancelAsync();
        await job.StopAsync(Cancel);

        Assert.Equal([TimeSpan.FromMinutes(3), TimeSpan.FromHours(2)], clock.Waits.Take(2).ToArray());
    }

    [Fact]
    public async Task ATickThatThrowsStillLetsTheNextOneStart()
    {
        var clock = new HurriedTicks();
        var recordings = new HeldRecordings { Refusing = new InvalidOperationException("the ledger is gone") };
        var driver = new RecordingDriver();
        using RecordingTickJob job = Job(Holding(Due(1)), recordings, driver, clock);
        using var stopping = new CancellationTokenSource();

        await job.StartAsync(stopping.Token);
        await Eventually.Happens(() => recordings.Listings >= 3, "the loop stopped at the first tick that threw");
        await stopping.CancelAsync();
        await job.StopAsync(Cancel);

        Assert.True(recordings.Listings >= 3);
    }

    [Fact]
    public async Task TheLoopStopsWhenItIsAskedTo()
    {
        var recordings = new HeldRecordings();
        using RecordingTickJob job = Job(Holding(), recordings, new RecordingDriver(), new HurriedTicks());
        using var stopping = new CancellationTokenSource();

        await job.StartAsync(stopping.Token);
        await Eventually.Happens(() => recordings.Listings >= 1, "the loop never ticked");
        await stopping.CancelAsync();
        await job.StopAsync(Cancel);

        int reached = recordings.Listings;

        await Task.Delay(50, Cancel);

        Assert.Equal(reached, recordings.Listings);
    }

    [Fact]
    public async Task ATickThatDidSomethingSaysSoAndOneThatDidNothingStaysQuiet()
    {
        var spoke = new WhatWasSaid();
        var idle = new WhatWasSaid();

        await Ticked(Once(Due(1)), spoke, until: (said, _) => said.Lines.Count >= 1);
        await Ticked(Holding(), idle, until: (_, ledger) => ledger.Listings >= 2);

        Assert.Contains(spoke.Lines, line => line.StartsWith("Information:", StringComparison.Ordinal));
        Assert.Empty(idle.Lines);

        Assert.Equal(1, spoke.Counted("Started"));
        Assert.Equal(0, spoke.Counted("Stopped"));
        Assert.Equal(0, spoke.Counted("Refused"));
        Assert.Equal(0, spoke.Counted("Unconfirmed"));
    }

    [Fact]
    public async Task EveryReservationThatDidNotStartIsNamedOnItsOwnLine()
    {
        var said = new WhatWasSaid();
        PlannedReservations reservations = Once(Due(1), Due(2));
        var driver = new RecordingDriver
        {
            RefusesToStart = DriverCall<SessionSnapshot>.Refused(new DriverProblem("noDeviceFree", [])),
        };

        await Ticked(reservations, said, driver, (spoken, _) => spoken.Lines.Count >= 3);

        Assert.Equal(2, said.Lines.Count(line => line.StartsWith("Warning:", StringComparison.Ordinal)));
        Assert.Single(said.Lines, line => line.StartsWith("Information:", StringComparison.Ordinal));
        Assert.Equal(2, said.Counted("Refused"));
        Assert.Equal(0, said.Counted("Started"));
    }

    private static async Task Ticked(
        PlannedReservations reservations,
        WhatWasSaid said,
        RecordingDriver? driver = null,
        Func<WhatWasSaid, HeldRecordings, bool>? until = null)
    {
        var clock = new HurriedTicks();
        var recordings = new HeldRecordings();
        RecordingDriver held = driver ?? new RecordingDriver();
        using RecordingTickJob job = Job(reservations, recordings, held, clock, said: said);
        using var stopping = new CancellationTokenSource();

        await job.StartAsync(stopping.Token);
        await Eventually.Happens(
            () => (until ?? ((spoken, _) => spoken.Lines.Count >= 1))(said, recordings),
            "the loop never got as far as the tick this test is about");
        await stopping.CancelAsync();
        await job.StopAsync(Cancel);
    }

    [Fact]
    public async Task ATickThatStartedARecordingAsksForTheAllocationToBeSettledAgain()
    {
        var clock = new HurriedTicks();
        var driver = new RecordingDriver();
        var notices = new CountedNotices();
        using RecordingTickJob job = Job(
            Once(Due(1)),
            new HeldRecordings(),
            driver,
            clock,
            notices: notices);
        using var stopping = new CancellationTokenSource();

        await job.StartAsync(stopping.Token);
        await Eventually.Happens(() => driver.Started.Count >= 1, "the loop never started a recording");
        await Eventually.Happens(() => notices.Nudged.Count >= 1, "the tick that started one asked for nothing");
        await stopping.CancelAsync();
        await job.StopAsync(Cancel);

        Assert.Equal([RecalculationTrigger.RecordingStarted], notices.Nudged.Distinct());
    }

    [Fact]
    public async Task ATickThatStoppedARecordingAsksForTheAllocationToBeSettledAgain()
    {
        var clock = new HurriedTicks();
        var recordings = new HeldRecordings();
        recordings.Rows.Add(InFlight(Airs.AddMinutes(-30), Airs));
        var driver = new RecordingDriver();
        var notices = new CountedNotices();
        using RecordingTickJob job = Job(Holding(), recordings, driver, clock, notices: notices);
        using var stopping = new CancellationTokenSource();

        await job.StartAsync(stopping.Token);
        await Eventually.Happens(() => notices.Nudged.Count >= 1, "the tick that stopped one asked for nothing");
        await stopping.CancelAsync();
        await job.StopAsync(Cancel);

        Assert.Equal([RecalculationTrigger.RecordingEnded], notices.Nudged.Distinct());
    }

    [Fact]
    public async Task ATickThatStartedAndStoppedNothingAsksForNothing()
    {
        var clock = new HurriedTicks();
        var recordings = new HeldRecordings();
        var notices = new CountedNotices();
        using RecordingTickJob job = Job(Holding(), recordings, new RecordingDriver(), clock, notices: notices);
        using var stopping = new CancellationTokenSource();

        await job.StartAsync(stopping.Token);
        await Eventually.Happens(() => recordings.Listings >= 2, "the loop never ticked twice");
        await stopping.CancelAsync();
        await job.StopAsync(Cancel);

        Assert.Empty(notices.Nudged);
    }

    private static PlannedReservations Holding(params RecordingTick[] ticks)
        => new PlannedReservations().Holding(ticks);

    private static PlannedReservations Once(params RecordingTick[] ticks)
    {
        PlannedReservations reservations = Holding(ticks);
        reservations.DueOnlyOnce = true;

        return reservations;
    }

    private static RecordingTickJob Job(
        PlannedReservations reservations,
        HeldRecordings recordings,
        RecordingDriver driver,
        TimeProvider clock,
        RecordingSettings? settings = null,
        WhatWasSaid? said = null,
        CountedNotices? notices = null)
    {
        RecordingSettings held = settings ?? RecordingSettings.Default;
        var services = new ServiceCollection();

        services.AddScoped(_ => new RecordingRound(
            reservations,
            recordings,
            new ResolvedTuning(Terrestrial),
            new DiskPrecheckService(new StorageMonitor(driver, clock, StorageMonitorSettings.Default)),
            driver,
            held,
            new HeldMoment(Airs)));

        return new RecordingTickJob(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            held,
            notices ?? new CountedNotices(),
            clock,
            said is null ? NullLogger<RecordingTickJob>.Instance : said.Logger());
    }

    private sealed class WhatWasSaid
    {
        private readonly List<string> lines = [];

        private readonly List<KeyValuePair<string, object?>> counted = [];

        public IReadOnlyList<string> Lines
        {
            get
            {
                lock (lines)
                {
                    return [.. lines];
                }
            }
        }

        public ILogger<RecordingTickJob> Logger() => new Listening(this);

        public int? Counted(string name)
        {
            lock (lines)
            {
                foreach (KeyValuePair<string, object?> pair in counted)
                {
                    if (pair.Key == name && pair.Value is int number)
                    {
                        return number;
                    }
                }
            }

            return null;
        }

        private void Heard(string line, IEnumerable<KeyValuePair<string, object?>>? named)
        {
            lock (lines)
            {
                lines.Add(line);

                if (named is not null)
                {
                    counted.AddRange(named);
                }
            }
        }

        private sealed class Listening(WhatWasSaid said) : ILogger<RecordingTickJob>
        {
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
                => said.Heard(
                    $"{logLevel}: {formatter(state, exception)}",
                    state as IEnumerable<KeyValuePair<string, object?>>);
        }
    }

    private sealed class HurriedTicks : TimeProvider
    {
        private readonly List<TimeSpan> waits = [];

        public IReadOnlyList<TimeSpan> Waits
        {
            get
            {
                lock (waits)
                {
                    return [.. waits];
                }
            }
        }

        public override DateTimeOffset GetUtcNow() => Airs;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            lock (waits)
            {
                waits.Add(dueTime);
            }

            return base.CreateTimer(callback, state, TimeSpan.FromMilliseconds(1), period);
        }
    }
}
