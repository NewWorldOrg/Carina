namespace Carina.Driver.Transport;

public sealed class ContinuityCounterTracker
{
    private readonly Dictionary<int, int> lastCounter = [];
    private readonly Dictionary<int, int> lastPayloadHash = [];
    private readonly Dictionary<int, long> dropsByPid = [];
    private readonly Lock gate = new();

    private long packets;
    private long drops;
    private long duplicates;
    private long discontinuities;
    private long transportErrors;
    private long scrambledPackets;
    private long provisionalPackets;

    public long Packets => Read(ref packets);

    public long Drops => Read(ref drops);

    public long Duplicates => Read(ref duplicates);

    public long Discontinuities => Read(ref discontinuities);

    public long TransportErrors => Read(ref transportErrors);

    public long ScrambledPackets => Read(ref scrambledPackets);

    public long ProvisionalPackets => Read(ref provisionalPackets);

    public long DropsFor(int pid)
    {
        lock (gate)
        {
            return dropsByPid.GetValueOrDefault(pid);
        }
    }

    public void Retuned()
    {
        lock (gate)
        {
            lastCounter.Clear();
            lastPayloadHash.Clear();
        }
    }

    public void Observe(TsPacket packet)
    {
        lock (gate)
        {
            Record(packet);
        }
    }

    private long Read(ref long counter)
    {
        lock (gate)
        {
            return counter;
        }
    }

    private void Record(TsPacket packet)
    {
        if (packet.Provisional)
        {
            provisionalPackets++;
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
            transportErrors++;
            return;
        }

        packets++;

        if (packet.Scrambled)
        {
            scrambledPackets++;
        }

        if (packet.Discontinuity)
        {
            discontinuities++;
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
                duplicates++;
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
        drops += missing;
        dropsByPid[pid] = dropsByPid.GetValueOrDefault(pid) + missing;
    }
}
