namespace Carina.Broadcast.Sections;

public sealed class SectionReader
{
    private readonly Dictionary<int, SectionAssembler> assemblers;

    public SectionReader(params int[] pids)
        : this((IEnumerable<int>)pids)
    {
    }

    public SectionReader(IEnumerable<int> pids)
    {
        ArgumentNullException.ThrowIfNull(pids);

        assemblers = pids.Distinct().ToDictionary(pid => pid, pid => new SectionAssembler(pid));
    }

    public IReadOnlyCollection<int> Pids => assemblers.Keys;

    public long UnreadablePackets { get; private set; }

    public IReadOnlyList<SectionRead> Push(ReadOnlySpan<byte> packets)
    {
        var outcomes = new List<SectionRead>();

        for (var at = 0; at < packets.Length; at += TransportPacket.Size)
        {
            if (packets.Length - at < TransportPacket.Size)
            {
                UnreadablePackets++;

                break;
            }

            var packet = packets.Slice(at, TransportPacket.Size);

            if (!TransportPacket.TryRead(packet, out var read))
            {
                UnreadablePackets++;

                continue;
            }

            if (assemblers.TryGetValue(read.Pid, out var assembler))
            {
                outcomes.AddRange(assembler.Push(packet));
            }
        }

        return outcomes;
    }

    public IReadOnlyList<SectionRead> Flush()
    {
        var outcomes = new List<SectionRead>();

        foreach (var assembler in assemblers.Values)
        {
            if (assembler.Flush() is { } outcome)
            {
                outcomes.Add(outcome);
            }
        }

        return outcomes;
    }

    public void Reset()
    {
        foreach (var assembler in assemblers.Values)
        {
            assembler.Reset();
        }
    }
}
