using System.Collections.Concurrent;

using Carina.Domain.Driver;
using Carina.Domain.Scans;
using Carina.Infrastructure.Driver;
using Carina.Infrastructure.Scanning;
using Carina.TestSupport;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Scanning;

public sealed class HurriedClock : TimeProvider
{
    private readonly ConcurrentQueue<TimeSpan> waits = new();

    public IReadOnlyCollection<TimeSpan> Waits => waits;

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
        => new Hurried(this, base.CreateTimer(callback, state, Remember(dueTime), period));

    private TimeSpan Remember(TimeSpan dueTime)
    {
        if (dueTime <= TimeSpan.Zero)
        {
            return dueTime;
        }

        waits.Enqueue(dueTime);

        return TimeSpan.Zero;
    }

    private sealed class Hurried(HurriedClock clock, ITimer inner) : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period)
            => inner.Change(clock.Remember(dueTime), period);

        public void Dispose() => inner.Dispose();

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}

public sealed class HeldClock : TimeProvider
{
    private readonly ConcurrentQueue<Action> pending = new();

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        if (dueTime > TimeSpan.Zero && dueTime != Timeout.InfiniteTimeSpan)
        {
            pending.Enqueue(() => callback(state));
        }

        return new Held();
    }

    public void FireOnePending(string what)
    {
        Assert.True(pending.TryDequeue(out var fire), $"No timer was waiting to be fired: {what}.");

        fire!();
    }

    private sealed class Held : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

public sealed class StillClock : TimeProvider
{
    public static readonly DateTimeOffset Now = new(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
        => base.CreateTimer(callback, state, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
}

public sealed class ScanHarness
{
    public ScanHarness(ScriptedDriverClient driver, TimeProvider? clock = null, ScanSettings? settings = null)
    {
        Driver = driver;
        Clock = clock ?? new StillClock();
        Settings = settings ?? ScanSettings.Default;

        Orchestrator = new ChannelScanOrchestrator(
            Driver,
            Signals,
            Runs,
            Services,
            Candidates,
            SatelliteStreams,
            Events,
            Clock,
            Settings);
    }

    public ScriptedDriverClient Driver { get; }

    public TimeProvider Clock { get; }

    public ScanSettings Settings { get; }

    public DriverSignalRelay Signals { get; } = new(NullLogger<DriverSignalRelay>.Instance);

    public HeldScanRuns Runs { get; } = new();

    public HeldServices Services { get; } = new();

    public HeldCandidates Candidates { get; } = new();

    public HeldSatelliteStreams SatelliteStreams { get; } = new();

    public RecordingAppEvents Events { get; } = new();

    public IChannelScanOrchestrator Orchestrator { get; }

    public void RestartTheDriver()
    {
        Driver.InstanceId = "instance-b";
        Signals.Publish(DriverClientSignals.InstanceChanged);
    }
}
