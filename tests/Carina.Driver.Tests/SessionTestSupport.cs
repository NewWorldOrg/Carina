using Carina.Driver.Tuning;

namespace Carina.Driver.Tests;

public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private long ticks = start.UtcTicks;

    public override DateTimeOffset GetUtcNow() =>
        new(Interlocked.Read(ref ticks), TimeSpan.Zero);

    public void Advance(TimeSpan by) => Interlocked.Add(ref ticks, by.Ticks);
}

public sealed class ScriptedTunerDevice(int failAfterReads = int.MaxValue) : ITunerDevice
{
    private readonly FakeTunerDevice inner = new(27, 1024);

    public int Reads { get; private set; }

    public bool Disposed { get; private set; }

    public byte[] Read(int count)
    {
        Reads++;

        if (Reads > failAfterReads)
        {
            throw new IOException("the device stopped answering");
        }

        return inner.Read(count);
    }

    public void Dispose() => Disposed = true;
}
