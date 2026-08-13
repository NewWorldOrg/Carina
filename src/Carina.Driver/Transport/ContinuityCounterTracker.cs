namespace Carina.Driver.Transport;

public sealed class ContinuityCounterTracker
{
    private const int CounterWrap = 16;

    private readonly Dictionary<int, int> lastCounter = [];
    private readonly Dictionary<int, long> dropsByPid = [];

    public long Packets { get; private set; }

    public long Drops { get; private set; }

    public long Duplicates { get; private set; }

    public long DropsFor(int pid) => dropsByPid.GetValueOrDefault(pid);

    public void Observe(TsPacket packet)
    {
        if (packet.IsNull)
        {
            return;
        }

        Packets++;

        if (!lastCounter.TryGetValue(packet.Pid, out var previous))
        {
            lastCounter[packet.Pid] = packet.ContinuityCounter;
            return;
        }

        if (!packet.HasPayload && packet.ContinuityCounter == previous)
        {
            return;
        }

        if (packet.ContinuityCounter == previous)
        {
            Duplicates++;
            return;
        }

        var expected = (previous + 1) % CounterWrap;
        if (packet.ContinuityCounter != expected)
        {
            var missing = (packet.ContinuityCounter - expected + CounterWrap) % CounterWrap;
            Drops += missing;
            dropsByPid[packet.Pid] = DropsFor(packet.Pid) + missing;
        }

        lastCounter[packet.Pid] = packet.ContinuityCounter;
    }
}
