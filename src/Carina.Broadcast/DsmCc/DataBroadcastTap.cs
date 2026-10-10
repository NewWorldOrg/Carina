using Carina.Broadcast.Sections;
using Carina.Broadcast.Tables;

namespace Carina.Broadcast.DsmCc;

/// <summary>
/// Reads one programme's data broadcast out of a transport stream handed over in pieces of any length: the
/// association table to the programme map, the map to the data streams, and each data stream's sections to
/// the carousels and the event messages, timed on the programme's clock followed through its wrap.
/// </summary>
/// <remarks>
/// Nothing is said of the data broadcast until the programme's clock has been heard. A new version of the
/// programme map starts the carousels and the event messages again.
/// </remarks>
public sealed class DataBroadcastTap
{
    public const int MostProgramNumber = 0xFFFF;

    private readonly int programNumber;

    private readonly DataCarousels carousels;

    private readonly ProgramClock clock = new();

    private readonly SectionAssembler association = new(ProgramAssociationTable.Pid);

    private readonly Dictionary<int, Component> components = [];

    private readonly byte[] partial = new byte[TransportPacket.Size];

    private int partialLength;

    private int? associationVersion;

    private SectionAssembler? map;

    private int? mapVersion;

    private int? clockPid;

    private DataBroadcastService? unannounced;

    private long? stamp;

    public DataBroadcastTap(int programNumber)
        : this(programNumber, CarouselLimits.Broadcast)
    {
    }

    public DataBroadcastTap(int programNumber, CarouselLimits limits)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(programNumber);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(programNumber, MostProgramNumber);
        ArgumentNullException.ThrowIfNull(limits);

        this.programNumber = programNumber;
        carousels = new DataCarousels(limits);
    }

    /// <summary>
    /// The moment what is read is stamped with: the programme's clock followed through its wrap, held where it
    /// was when the clock steps back, or null until the clock has been heard.
    /// </summary>
    public long? Now => stamp;

    /// <summary>
    /// The first moment of the programme's clock that was heard, or null until it has been heard.
    /// </summary>
    public long? FirstHeard { get; private set; }

    public long UnreadablePackets { get; private set; }

    public long RejectedSections { get; private set; }

    public IReadOnlyList<DataBroadcastRead> Push(ReadOnlySpan<byte> bytes)
    {
        List<DataBroadcastRead> reads = [];
        ReadOnlySpan<byte> rest = Completed(bytes, reads);

        while (!rest.IsEmpty)
        {
            int sync = rest.IndexOf(TransportPacket.SyncByte);

            if (sync != 0)
            {
                UnreadablePackets++;
                rest = sync < 0 ? [] : rest[sync..];

                continue;
            }

            if (rest.Length < TransportPacket.Size)
            {
                rest.CopyTo(partial);
                partialLength = rest.Length;

                break;
            }

            Read(rest[..TransportPacket.Size], reads);
            rest = rest[TransportPacket.Size..];
        }

        return reads;
    }

    private ReadOnlySpan<byte> Completed(ReadOnlySpan<byte> bytes, List<DataBroadcastRead> reads)
    {
        if (partialLength == 0)
        {
            return bytes;
        }

        int wanted = TransportPacket.Size - partialLength;

        if (bytes.Length < wanted)
        {
            bytes.CopyTo(partial.AsSpan(partialLength));
            partialLength += bytes.Length;

            return [];
        }

        bytes[..wanted].CopyTo(partial.AsSpan(partialLength));
        partialLength = 0;
        Read(partial, reads);

        return bytes[wanted..];
    }

    private void Read(ReadOnlySpan<byte> packet, List<DataBroadcastRead> reads)
    {
        if (!TransportPacket.TryRead(packet, out TransportPacket read))
        {
            UnreadablePackets++;

            return;
        }

        if (read.Pid == clockPid && !read.TransportError && read.ProgramClockReference is { } reference)
        {
            long followed = clock.Follow(reference);

            stamp = stamp is { } before && before > followed ? before : followed;
            FirstHeard ??= followed;
        }

        if (read.Pid == association.Pid)
        {
            Associate(association.Push(packet));
        }
        else if (map is not null && read.Pid == map.Pid)
        {
            Map(map.Push(packet));
        }

        if (stamp is not { } now)
        {
            return;
        }

        if (unannounced is { } service)
        {
            reads.Add(new DataBroadcastRead.ServiceMapped(now, service));
            unannounced = null;
        }

        if (components.TryGetValue(read.Pid, out Component? component))
        {
            Carry(component, component.Sections.Push(packet), now, reads);
        }
    }

    private void Associate(IReadOnlyList<SectionRead> sections)
    {
        foreach (SectionRead read in sections)
        {
            if (Current(read) is not { } section
                || ProgramAssociationTable.Read(section) is not TableRead<ProgramAssociationTable>.Parsed { Table: ProgramAssociationTable table }
                || table.VersionNumber == associationVersion)
            {
                continue;
            }

            associationVersion = table.VersionNumber;
            int? mapPid = table.MapPidOf(programNumber);

            if (mapPid != map?.Pid)
            {
                map = mapPid is { } pid ? new SectionAssembler(pid) : null;
                mapVersion = null;
            }
        }
    }

    private void Map(IReadOnlyList<SectionRead> sections)
    {
        foreach (SectionRead read in sections)
        {
            if (Current(read) is not { } section
                || ProgramMapTable.Read(section) is not TableRead<ProgramMapTable>.Parsed { Table: ProgramMapTable table }
                || table.ProgramNumber != programNumber
                || table.VersionNumber == mapVersion)
            {
                continue;
            }

            mapVersion = table.VersionNumber;
            clockPid = table.PcrPid;
            Start(DataBroadcastStreams.Find(table));
        }
    }

    private void Start(DataBroadcastService service)
    {
        carousels.Reset();
        components.Clear();

        foreach (DataBroadcastStream stream in service.Streams)
        {
            components[stream.Pid] = new Component(stream.ComponentTag, new SectionAssembler(stream.Pid), new EventMessageClock());
        }

        unannounced = service;
    }

    private void Carry(Component component, IReadOnlyList<SectionRead> sections, long now, List<DataBroadcastRead> reads)
    {
        foreach (SectionRead read in sections)
        {
            if (Current(read) is not { } section)
            {
                continue;
            }

            if (section.TableId == StreamDescriptorSection.TableId)
            {
                Time(component, section, now, reads);

                continue;
            }

            foreach (CarouselChange change in carousels.Push(component.Tag, section))
            {
                reads.Add(new DataBroadcastRead.CarouselChanged(now, change));
            }
        }
    }

    private void Time(Component component, Section section, long now, List<DataBroadcastRead> reads)
    {
        if (StreamDescriptorSection.Read(section) is not TableRead<StreamDescriptorSection>.Parsed { Table: StreamDescriptorSection descriptors })
        {
            RejectedSections++;

            return;
        }

        foreach (EventMessageOutcome outcome in component.Events.Push(descriptors, now))
        {
            reads.Add(outcome switch
            {
                EventMessageOutcome.Timed timed
                    => new DataBroadcastRead.EventMessageTimed(now, timed.Message, clock.Place(timed.Message.FiresAt) ?? now),
                EventMessageOutcome.Discarded discarded => new DataBroadcastRead.EventMessageDiscarded(now, discarded.Defect),
                _ => throw new ArgumentOutOfRangeException(nameof(component), outcome, "An event message is timed or discarded."),
            });
        }
    }

    private Section? Current(SectionRead read)
    {
        if (read is SectionRead.Assembled assembled)
        {
            return assembled.Section.IsCurrent ? assembled.Section : null;
        }

        RejectedSections++;

        return null;
    }

    private sealed record Component(int Tag, SectionAssembler Sections, EventMessageClock Events);
}
