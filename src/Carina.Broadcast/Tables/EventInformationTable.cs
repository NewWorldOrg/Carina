using Carina.Broadcast.Descriptors;
using Carina.Broadcast.Sections;

namespace Carina.Broadcast.Tables;

public sealed class EventInformationTable
{
    public const int Pid = 0x0012;

    public const int PresentFollowingActualTableId = 0x4E;

    public const int FirstScheduleActualTableId = 0x50;

    public const int LastScheduleActualTableId = 0x5F;

    private const int HeaderSize = 6;

    private const int EventHeaderSize = 12;

    private EventInformationTable(
        Section section,
        ReadOnlySpan<byte> header,
        IReadOnlyList<DescribedEvent> events,
        int discarded)
    {
        DiscardedEvents = discarded;
        ServiceId = section.TableIdExtension;
        TransportStreamId = (header[0] << 8) | header[1];
        OriginalNetworkId = (header[2] << 8) | header[3];
        SegmentLastSectionNumber = header[4];
        LastTableId = header[5];
        TableId = section.TableId;
        VersionNumber = section.VersionNumber;
        SectionNumber = section.SectionNumber;
        LastSectionNumber = section.LastSectionNumber;
        Events = events;
    }

    public int ServiceId { get; }

    public int TransportStreamId { get; }

    public int OriginalNetworkId { get; }

    public int SegmentLastSectionNumber { get; }

    public int LastTableId { get; }

    public int TableId { get; }

    public int VersionNumber { get; }

    public int SectionNumber { get; }

    public int LastSectionNumber { get; }

    public IReadOnlyList<DescribedEvent> Events { get; }

    public int DiscardedEvents { get; }

    public bool IsPresentFollowing => TableId == PresentFollowingActualTableId;

    public static bool CarriesEvents(int tableId)
        => tableId == PresentFollowingActualTableId
            || tableId is >= FirstScheduleActualTableId and <= LastScheduleActualTableId;

    public static TableRead<EventInformationTable> Read(Section section)
    {
        ArgumentNullException.ThrowIfNull(section);

        if (!CarriesEvents(section.TableId))
        {
            return Rejected(TableDefect.WrongTableId);
        }

        var body = section.Body;

        if (body.Length < HeaderSize)
        {
            return Rejected(TableDefect.SectionTooShort);
        }

        var span = body.Span;
        var events = new List<DescribedEvent>();
        var discarded = 0;
        var at = HeaderSize;

        while (at < body.Length)
        {
            if (body.Length - at < EventHeaderSize)
            {
                return Rejected(TableDefect.LoopOverrun);
            }

            var descriptorsLength = ((span[at + 10] & 0x0F) << 8) | span[at + 11];

            if (at + EventHeaderSize + descriptorsLength > body.Length)
            {
                return Rejected(TableDefect.LoopOverrun);
            }

            if (!DescriptorLoop.TryRead(body.Slice(at + EventHeaderSize, descriptorsLength), out var descriptors))
            {
                return Rejected(TableDefect.MalformedDescriptor);
            }

            if (!BroadcastTime.TryReadStart(span.Slice(at + 2, BroadcastTime.StartSize), out var startsAt)
                || !BroadcastTime.TryReadDuration(span.Slice(at + 7, BroadcastTime.DurationSize), out var runs))
            {
                discarded++;
                at += EventHeaderSize + descriptorsLength;

                continue;
            }

            events.Add(new DescribedEvent(
                (span[at] << 8) | span[at + 1],
                startsAt.Value,
                runs,
                Status(span[at + 10] >> 5),
                (span[at + 10] & 0x10) != 0,
                descriptors));

            at += EventHeaderSize + descriptorsLength;
        }

        return new TableRead<EventInformationTable>.Parsed(
            new EventInformationTable(section, span[..HeaderSize], events, discarded));
    }

    private static RunningStatus Status(int declared)
        => declared is >= (int)RunningStatus.Undefined and <= (int)RunningStatus.OffAir
            ? (RunningStatus)declared
            : RunningStatus.Undefined;

    private static TableRead<EventInformationTable> Rejected(TableDefect defect)
        => new TableRead<EventInformationTable>.Rejected(defect);
}
