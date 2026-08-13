using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Recording;
using Carina.Driver.Tuning;

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
    private readonly FakeTunerDevice inner = new(27, 1024);
    private long reads;

    public long Reads => Interlocked.Read(ref reads);

    public bool Disposed { get; private set; }

    public byte[] Read(int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var taken = Interlocked.Increment(ref reads);

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

public sealed class StubbornTunerDevice(TimeSpan readTakes) : ITunerDevice
{
    private readonly FakeTunerDevice inner = new(27, 1024);

    public ManualResetEventSlim Reading { get; } = new();

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
    public ITunerDevice Create(DeviceSettings device, TuningRequest tuning) =>
        new ScriptedTunerDevice(failAfterReads);
}

public sealed class StubbornTunerDeviceFactory(TimeSpan readTakes) : ITunerDeviceFactory
{
    public ITunerDevice Create(DeviceSettings device, TuningRequest tuning) =>
        new StubbornTunerDevice(readTakes);
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
