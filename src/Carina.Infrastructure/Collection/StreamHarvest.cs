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
    int RejectedTables)
{
    public IReadOnlyList<ServiceDescriptionTable> Descriptions { get; init; } = [];
}

public sealed record Gathered(
    IReadOnlyList<EventInformationTable> Tables,
    IReadOnlyList<ScheduledService> HeardWhole);

public sealed class StreamHarvest(TimeProvider clock)
{
    private readonly SectionReader reader = new(EventInformationTable.Pid, ServiceDescriptionTable.Pid);

    private readonly List<EventInformationTable> tables = [];

    private readonly ScheduleProgress progress = new(clock);

    private ScheduleProgress sinceTaken = new(clock);

    private readonly Dictionary<(bool Actual, int Network, int Stream, int Section), ServiceDescriptionTable> descriptions = [];

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

            if (assembled.Pid == ServiceDescriptionTable.Pid)
            {
                if (ServiceDescriptionTable.Read(assembled.Section)
                    is TableRead<ServiceDescriptionTable>.Parsed described)
                {
                    ServiceDescriptionTable table = described.Table;

                    descriptions[(table.IsActualStream, table.OriginalNetworkId, table.TransportStreamId, table.SectionNumber)] = table;
                }

                continue;
            }

            if (EventInformationTable.Read(assembled.Section) is not TableRead<EventInformationTable>.Parsed parsed)
            {
                rejectedTables++;

                continue;
            }

            tables.Add(parsed.Table);
            progress.Saw(parsed.Table);
            sinceTaken.Saw(parsed.Table);
        }
    }

    /// <summary>
    /// Hands over the tables read since the last take, with the services whose whole schedule those
    /// tables alone carry.
    /// </summary>
    public Gathered TakeWhatIsGathered()
    {
        if (tables.Count == 0)
        {
            return new Gathered([], []);
        }

        Gathered taken = new([.. tables], sinceTaken.HeardWhole());

        tables.Clear();
        sinceTaken = new ScheduleProgress(clock);

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
        => new(outcome, progress, [.. tables], reader.UnreadablePackets, rejectedSections, rejectedTables)
        {
            Descriptions = [.. descriptions.Values],
        };
}
