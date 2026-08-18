using Carina.Broadcast.Sections;
using Carina.Broadcast.Tables;
using Carina.Domain.Programmes;

namespace Carina.Infrastructure.Collection;

public sealed record HarvestedStream(
    VisitOutcome Outcome,
    ScheduleProgress Progress,
    IReadOnlyList<EventInformationTable> Tables,
    long UnreadablePackets,
    int RejectedSections,
    int RejectedTables);

public sealed class StreamHarvest
{
    private readonly SectionReader reader = new(EventInformationTable.Pid);

    private readonly List<EventInformationTable> tables = [];

    private readonly ScheduleProgress progress = new();

    private int rejectedSections;

    private int rejectedTables;

    public ScheduleProgress Progress => progress;

    public long UnreadablePackets => reader.UnreadablePackets;

    public bool CanLetGo => progress.Completeness is not ScheduleCompleteness.Incomplete;

    private readonly byte[] carry = new byte[TransportPacket.Size];

    private int carried;

    public void Push(ReadOnlySpan<byte> packets)
    {
        if (carried > 0)
        {
            int take = Math.Min(TransportPacket.Size - carried, packets.Length);

            packets[..take].CopyTo(carry.AsSpan(carried));
            carried += take;
            packets = packets[take..];

            if (carried < TransportPacket.Size)
            {
                return;
            }

            Read(carry);
            carried = 0;
        }

        int whole = packets.Length - (packets.Length % TransportPacket.Size);

        Read(packets[..whole]);
        packets[whole..].CopyTo(carry);
        carried = packets.Length % TransportPacket.Size;
    }

    private void Read(ReadOnlySpan<byte> packets)
    {
        foreach (SectionRead read in reader.Push(packets))
        {
            if (read is not SectionRead.Assembled assembled)
            {
                rejectedSections++;

                continue;
            }

            if (EventInformationTable.Read(assembled.Section) is not TableRead<EventInformationTable>.Parsed parsed)
            {
                rejectedTables++;

                continue;
            }

            tables.Add(parsed.Table);
            progress.Saw(parsed.Table);
        }
    }

    public IReadOnlyList<EventInformationTable> TakeWhatIsGathered()
    {
        if (tables.Count == 0)
        {
            return [];
        }

        EventInformationTable[] taken = [.. tables];

        tables.Clear();

        return taken;
    }

    public HarvestedStream Conclude(bool interrupted, bool anyBytes)
    {
        if (interrupted)
        {
            return Harvested(VisitOutcome.Interrupted);
        }

        if (!anyBytes)
        {
            return Harvested(VisitOutcome.NoBytes);
        }

        return Harvested(progress.Completeness switch
        {
            ScheduleCompleteness.Complete => VisitOutcome.Complete,
            ScheduleCompleteness.BasicOnly => VisitOutcome.BasicOnly,
            _ => VisitOutcome.Incomplete,
        });
    }

    private HarvestedStream Harvested(VisitOutcome outcome)
        => new(outcome, progress, [.. tables], reader.UnreadablePackets, rejectedSections, rejectedTables);
}
