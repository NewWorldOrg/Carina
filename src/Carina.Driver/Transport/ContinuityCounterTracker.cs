namespace Carina.Driver.Transport;

public sealed class ContinuityCounterTracker
{
    private readonly Dictionary<int, int> lastCounter = [];
    private readonly Dictionary<int, int> lastPayloadHash = [];
    private readonly Dictionary<int, long> dropsByPid = [];

    public long Packets { get; private set; }

    public long Drops { get; private set; }

    public long Duplicates { get; private set; }

    public long Discontinuities { get; private set; }

    public long TransportErrors { get; private set; }

    public long ScrambledPackets { get; private set; }

    public long DropsFor(int pid) => dropsByPid.GetValueOrDefault(pid);

    public void Retuned()
    {
        lastCounter.Clear();
        lastPayloadHash.Clear();
    }

    public long ProvisionalPackets { get; private set; }

    public void Observe(TsPacket packet)
    {
        if (packet.Provisional)
        {
            ProvisionalPackets++;
            return;
        }

        if (packet.Pid is < 0 or > TsPacket.MaxPid)
        {
            return;
        }

        if (packet.ContinuityCounter is < 0 or >= TsPacket.CounterWrap)
        {
            return;
        }

        if (packet.IsNull)
        {
            return;
        }

        if (packet.TransportError)
        {
            TransportErrors++;
            return;
        }

        Packets++;

        if (packet.Scrambled)
        {
            ScrambledPackets++;
        }

        if (packet.Discontinuity)
        {
            Discontinuities++;
            Remember(packet);
            return;
        }

        if (!lastCounter.TryGetValue(packet.Pid, out var previous))
        {
            Remember(packet);
            return;
        }

        if (!packet.HasPayload && packet.ContinuityCounter == previous)
        {
            return;
        }

        if (packet.ContinuityCounter == previous)
        {
            if (lastPayloadHash.GetValueOrDefault(packet.Pid) == packet.PayloadHash)
            {
                Duplicates++;
                return;
            }

            Count(packet.Pid, TsPacket.CounterWrap);
            Remember(packet);
            return;
        }

        var expected = (previous + 1) % TsPacket.CounterWrap;
        if (packet.ContinuityCounter != expected)
        {
            var missing =
                (packet.ContinuityCounter - expected + TsPacket.CounterWrap)
                % TsPacket.CounterWrap;
            Count(packet.Pid, missing);
        }

        Remember(packet);
    }

    private void Remember(TsPacket packet)
    {
        lastCounter[packet.Pid] = packet.ContinuityCounter;
        lastPayloadHash[packet.Pid] = packet.PayloadHash;
    }

    private void Count(int pid, long missing)
    {
        Drops += missing;
        dropsByPid[pid] = DropsFor(pid) + missing;
    }
}
