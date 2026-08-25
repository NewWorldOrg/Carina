using System.Collections.Concurrent;

using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Recording;
using Carina.Driver.Tuning;

using Microsoft.Extensions.Logging;

namespace Carina.Driver.Tests;

public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private long ticks = start.UtcTicks;

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);

    public void Advance(TimeSpan by) => Interlocked.Add(ref ticks, by.Ticks);
}

public sealed class ScriptedTunerDevice(
    int failAfterReads = int.MaxValue,
    int emptyAfterReads = int.MaxValue
) : ITunerDevice
{
    private readonly FakeTunerDevice inner = new(55, 50001);
    private long reads;

    public long Reads => Interlocked.Read(ref reads);

    public long Overflows { get; set; }

    public bool Disposed { get; private set; }

    public byte[] Read(int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        long taken = Interlocked.Increment(ref reads);

        if (taken > failAfterReads)
        {
            throw new IOException("the device stopped answering");
        }

        if (taken > emptyAfterReads)
        {
            return [];
        }

        return inner.Read(count, cancellationToken);
    }

    public void Dispose() => Disposed = true;
}

public sealed class PacedTunerDevice : ITunerDevice
{
    private static readonly TimeSpan Deadlock = TimeSpan.FromSeconds(30);

    private readonly FakeTunerDevice inner = new(55, 50001);
    private readonly SemaphoreSlim allowed = new(0);
    private readonly SemaphoreSlim parked = new(0);

    private long reads;
    private int seen;

    public long Reads => Interlocked.Read(ref reads);

    public long Overflows { get; set; }

    public ScriptedQualitySource? Signal { get; init; }

    public ISignalQualitySource? Quality => Signal;

    public bool Disposed { get; private set; }

    public byte[] Read(int count, CancellationToken cancellationToken)
    {
        parked.Release();
        allowed.Wait(cancellationToken);
        Interlocked.Increment(ref reads);

        return inner.Read(count, cancellationToken);
    }

    public void Allow(int chunks) => allowed.Release(chunks);

    public void AwaitParkedBefore(int read)
    {
        while (seen < read)
        {
            Assert.True(
                parked.Wait(Deadlock),
                $"The session never settled before read {seen + 1}; it is stuck on a subscriber."
            );

            seen++;
        }
    }

    public void Dispose() => Disposed = true;
}

public sealed class HeldOpenTunerDevice : ITunerDevice
{
    private readonly FakeTunerDevice inner = new(55, 50001);
    private readonly ManualResetEventSlim gate = new(false);

    public ManualResetEventSlim Reading { get; } = new(false);

    public long Overflows => 0;

    public bool Disposed { get; private set; }

    public byte[] Read(int count, CancellationToken cancellationToken)
    {
        Reading.Set();
        gate.Wait(CancellationToken.None);
        cancellationToken.ThrowIfCancellationRequested();

        return inner.Read(count, cancellationToken);
    }

    public void LetGo() => gate.Set();

    public void Dispose() => Disposed = true;
}

public sealed class OneTunerDeviceFactory(ITunerDevice device) : ITunerDeviceFactory
{
    private long created;

    public long Created => Interlocked.Read(ref created);

    public ITunerDevice Create(DeviceSettings settings, TuningRequest tuning, TuneParams? tune)
    {
        Interlocked.Increment(ref created);

        return device;
    }
}

public sealed class StubbornTunerDevice(TimeSpan readTakes) : ITunerDevice
{
    private readonly FakeTunerDevice inner = new(55, 50001);

    public ManualResetEventSlim Reading { get; } = new();

    public long Overflows => 0;

    public bool Disposed { get; private set; }

    public byte[] Read(int count, CancellationToken cancellationToken)
    {
        Reading.Set();
        Thread.Sleep(readTakes);

        return inner.Read(count, cancellationToken);
    }

    public void Dispose() => Disposed = true;
}

public sealed class ScriptedTunerDeviceFactory(int failAfterReads = int.MaxValue)
    : ITunerDeviceFactory
{
    public ITunerDevice Create(DeviceSettings device, TuningRequest tuning, TuneParams? tune) =>
        new ScriptedTunerDevice(failAfterReads);
}

public sealed class StubbornTunerDeviceFactory(TimeSpan readTakes) : ITunerDeviceFactory
{
    public ITunerDevice Create(DeviceSettings device, TuningRequest tuning, TuneParams? tune) =>
        new StubbornTunerDevice(readTakes);
}

public sealed class StubbornForOneDeviceFactory(string stubbornDeviceId, TimeSpan readTakes)
    : ITunerDeviceFactory
{
    public ITunerDevice Create(DeviceSettings device, TuningRequest tuning, TuneParams? tune) =>
        device.Id == stubbornDeviceId
            ? new StubbornTunerDevice(readTakes)
            : new ScriptedTunerDevice();
}

public sealed class SelectiveTunerDeviceFactory(string failingDeviceId, int failAfterReads = 1)
    : ITunerDeviceFactory
{
    public ITunerDevice Create(DeviceSettings device, TuningRequest tuning, TuneParams? tune) =>
        device.Id == failingDeviceId
            ? new ScriptedTunerDevice(failAfterReads)
            : new ScriptedTunerDevice();
}

public sealed class CountingRecordingWriter : IRecordingWriter
{
    private long bytesWritten;

    public CountingRecordingWriter(string path, bool failOnClose = false)
    {
        Path = path;
        FailOnClose = failOnClose;
    }

    public string Path { get; }

    public bool FailOnClose { get; }

    public bool Disposed { get; private set; }

    public long BytesWritten => Interlocked.Read(ref bytesWritten);

    public void Write(ReadOnlySpan<byte> bytes) => Interlocked.Add(ref bytesWritten, bytes.Length);

    public void Dispose()
    {
        Disposed = true;

        if (FailOnClose)
        {
            throw new IOException("the recording could not be closed");
        }
    }
}

public sealed class CountingRecordingWriterFactory : IRecordingWriterFactory
{
    private long opened;

    public long Opened => Interlocked.Read(ref opened);

    public CountingRecordingWriter? Last { get; private set; }

    public IRecordingWriter Open(string recordingsDirectory, string recordingId)
    {
        Interlocked.Increment(ref opened);

        Last = new CountingRecordingWriter(
            System.IO.Path.Combine(recordingsDirectory, $"{recordingId}.ts")
        );

        return Last;
    }
}

public sealed class StallingRecordingWriterFactory : IRecordingWriterFactory
{
    private readonly SemaphoreSlim opening = new(0);
    private readonly SemaphoreSlim released = new(0);

    public IRecordingWriter Open(string recordingsDirectory, string recordingId)
    {
        opening.Release();
        released.Wait();

        return new CountingRecordingWriter(
            System.IO.Path.Combine(recordingsDirectory, $"{recordingId}.ts")
        );
    }

    public void AwaitOpening(TimeSpan within) =>
        Assert.True(
            opening.Wait(within),
            "The driver never reached the point of opening a recording file."
        );

    public void LetGo() => released.Release();
}

public sealed class BrittleRecordingWriter(string path, long failAfterBytes = 0)
    : IRecordingWriter
{
    private long bytesWritten;

    public string Path { get; } = path;

    public bool Disposed { get; private set; }

    public long BytesWritten => Interlocked.Read(ref bytesWritten);

    public void Write(ReadOnlySpan<byte> bytes)
    {
        if (BytesWritten + bytes.Length > failAfterBytes)
        {
            throw new IOException("No space left on device");
        }

        Interlocked.Add(ref bytesWritten, bytes.Length);
    }

    public void Dispose() => Disposed = true;
}

public sealed class BrittleRecordingWriterFactory(long failAfterBytes = 0)
    : IRecordingWriterFactory
{
    private long opened;

    public long Opened => Interlocked.Read(ref opened);

    public IRecordingWriter Open(string recordingsDirectory, string recordingId)
    {
        Interlocked.Increment(ref opened);

        return new BrittleRecordingWriter(
            System.IO.Path.Combine(recordingsDirectory, $"{recordingId}.ts"),
            failAfterBytes
        );
    }
}

public sealed class StallingRecordingWriter(string path, long stallAfterBytes)
    : IRecordingWriter
{
    private readonly SemaphoreSlim released = new(0);
    private readonly SemaphoreSlim stalled = new(0);

    private long bytesWritten;
    private int held;

    public string Path { get; } = path;

    public bool Disposed { get; private set; }

    public long BytesWritten => Interlocked.Read(ref bytesWritten);

    public void Write(ReadOnlySpan<byte> bytes)
    {
        if (BytesWritten >= stallAfterBytes && Interlocked.Exchange(ref held, 1) is 0)
        {
            stalled.Release();
            released.Wait();
        }

        Interlocked.Add(ref bytesWritten, bytes.Length);
    }

    public void AwaitStall(TimeSpan within) =>
        Assert.True(stalled.Wait(within), "The writer was never asked to take a chunk it could not take.");

    public void LetGo() => released.Release();

    public void Dispose()
    {
        Disposed = true;
        released.Release();
    }
}

public sealed class OneRecordingWriterFactory(IRecordingWriter writer) : IRecordingWriterFactory
{
    public IRecordingWriter Open(string recordingsDirectory, string recordingId) => writer;
}

public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<string> lines = new();

    public IReadOnlyCollection<string> Lines => [.. lines];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    ) => lines.Enqueue($"{logLevel} {formatter(state, exception)}");
}

public sealed class SteppedTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly Lock gate = new();
    private readonly List<SteppedTimer> waiting = [];

    private long ticks = start.UtcTicks;

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);

    public int Waiting
    {
        get
        {
            lock (gate)
            {
                return waiting.Count;
            }
        }
    }

    public void Advance(TimeSpan by)
    {
        Interlocked.Add(ref ticks, by.Ticks);

        SteppedTimer[] due;

        lock (gate)
        {
            due = [.. waiting];
        }

        foreach (SteppedTimer timer in due)
        {
            timer.FireIfDue(GetUtcNow());
        }
    }

    public void AwaitSomethingWaitingOnTheClock(TimeSpan within)
    {
        DateTime deadline = DateTime.UtcNow + within;

        while (Waiting is 0)
        {
            Assert.True(
                DateTime.UtcNow < deadline,
                "Nothing ever asked this clock to wake it, so advancing it would prove nothing."
            );

            Thread.Sleep(1);
        }
    }

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period
    )
    {
        var timer = new SteppedTimer(this, callback, state);

        timer.Change(dueTime, period);

        return timer;
    }

    internal void Keep(SteppedTimer timer)
    {
        lock (gate)
        {
            if (!waiting.Contains(timer))
            {
                waiting.Add(timer);
            }
        }
    }

    internal void Forget(SteppedTimer timer)
    {
        lock (gate)
        {
            waiting.Remove(timer);
        }
    }
}

public sealed class SteppedTimer(
    SteppedTimeProvider clock,
    TimerCallback callback,
    object? state
) : ITimer
{
    private readonly Lock gate = new();

    private DateTimeOffset? dueAt;
    private TimeSpan period = Timeout.InfiniteTimeSpan;

    public bool Change(TimeSpan dueTime, TimeSpan every)
    {
        lock (gate)
        {
            period = every;
            dueAt = dueTime == Timeout.InfiniteTimeSpan ? null : clock.GetUtcNow() + dueTime;
        }

        if (dueAt is null)
        {
            clock.Forget(this);

            return true;
        }

        clock.Keep(this);
        FireIfDue(clock.GetUtcNow());

        return true;
    }

    public void Dispose()
    {
        lock (gate)
        {
            dueAt = null;
        }

        clock.Forget(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();

        return ValueTask.CompletedTask;
    }

    internal void FireIfDue(DateTimeOffset now)
    {
        lock (gate)
        {
            if (dueAt is not { } at || now < at)
            {
                return;
            }

            dueAt = period == Timeout.InfiniteTimeSpan ? null : now + period;
        }

        if (dueAt is null)
        {
            clock.Forget(this);
        }

        callback(state);
    }
}

public sealed class RationedRecordingWriter(IRecordingWriter inner, long room) : IRecordingWriter
{
    public string Path => inner.Path;

    public long BytesWritten => inner.BytesWritten;

    public void Write(ReadOnlySpan<byte> bytes)
    {
        long left = room - inner.BytesWritten;

        if (left >= bytes.Length)
        {
            inner.Write(bytes);

            return;
        }

        if (left > 0)
        {
            inner.Write(bytes[..(int)left]);
        }

        throw new IOException("No space left on device");
    }

    public void Dispose() => inner.Dispose();
}

public sealed class RationedRecordingWriterFactory(long room) : IRecordingWriterFactory
{
    private long opened;

    public long Opened => Interlocked.Read(ref opened);

    public IRecordingWriter Open(string recordingsDirectory, string recordingId)
    {
        Interlocked.Increment(ref opened);

        return new RationedRecordingWriter(
            new RecordingWriter(recordingsDirectory, recordingId),
            room
        );
    }
}
