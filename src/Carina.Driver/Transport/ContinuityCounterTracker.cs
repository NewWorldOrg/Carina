using Carina.Contracts;

namespace Carina.Driver.Transport;

public sealed class ContinuityCounterTracker
{
    private readonly Dictionary<int, int> lastCounter = [];
    private readonly Dictionary<int, int> lastPayloadHash = [];
    private readonly Dictionary<int, long> dropsByPid = [];
    private readonly SortedDictionary<int, (long Continuity, long Scrambled)> buckets = [];
    private readonly PcrTimeline timeline = new();
    private readonly Lock gate = new();

    private long packets;
    private long drops;
    private long duplicates;
    private long discontinuities;
    private long transportErrors;
    private long scrambledPackets;
    private long provisionalPackets;

    public SessionCounters Snapshot()
    {
        lock (gate)
        {
            bool measured = packets > 0;

            return new SessionCounters(
                packets,
                drops,
                duplicates,
                discontinuities,
                transportErrors,
                scrambledPackets,
                provisionalPackets,
                CcMeasured: measured,
                ScrambleMeasured: measured,
                Positions: WhereTheyWere()
            );
        }
    }

    private DropPositionsDto? WhereTheyWere() =>
        timeline.Anchor is { } anchor
            ? new DropPositionsDto(anchor, [.. Placed()], timeline.Reanchors)
            : null;

    private IEnumerable<DropBucketDto> Placed() =>
        buckets.Select(bucket => new DropBucketDto(
            bucket.Key,
            bucket.Value.Continuity,
            bucket.Value.Scrambled
        ));

    public long DropsFor(int pid)
    {
        lock (gate)
        {
            return dropsByPid.GetValueOrDefault(pid);
        }
    }

    public void Observe(TsPacket packet)
    {
        lock (gate)
        {
            Record(packet);
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

        if (packet.Pcr is { } reference)
        {
            timeline.Observe(packet.Pid, reference, packet.Discontinuity);
        }

        packets++;

        if (packet.Scrambled)
        {
            scrambledPackets++;
            Locate(0, 1);
        }

        if (packet.Discontinuity)
        {
            discontinuities++;
            Remember(packet);
            return;
        }

        if (!lastCounter.TryGetValue(packet.Pid, out int previous))
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

        int expected = (previous + 1) % TsPacket.CounterWrap;
        if (packet.ContinuityCounter != expected)
        {
            int missing =
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
        Locate(missing, 0);
    }

    private void Locate(long continuity, long scrambled)
    {
        (long lost, long unresolved) = buckets.GetValueOrDefault(timeline.Second);

        buckets[timeline.Second] = (lost + continuity, unresolved + scrambled);
    }
}
