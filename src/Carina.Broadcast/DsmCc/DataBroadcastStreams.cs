using Carina.Broadcast.Descriptors;
using Carina.Broadcast.Tables;

namespace Carina.Broadcast.DsmCc;

public static class DataBroadcastStreams
{
    public const int DsmCcSectionsStreamType = 0x0D;

    public const int BxmlDataComponentId = 0x000C;

    public const int EntryComponentTag = 0x40;

    public const int LastDataComponentTag = 0x7F;

    private const int DataComponentIdSize = 2;

    public static DataBroadcastService Find(ProgramMapTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        List<DataBroadcastStream> found = [];
        List<DataBroadcastStreamDefect> defects = [];

        foreach (ElementaryStream stream in table.Streams)
        {
            if (Read(stream, defects) is { } carried)
            {
                found.Add(carried);
            }
        }

        return new DataBroadcastService(found, defects);
    }

    private static DataBroadcastStream? Read(ElementaryStream stream, List<DataBroadcastStreamDefect> defects)
    {
        if (stream.StreamType != DsmCcSectionsStreamType)
        {
            return null;
        }

        Descriptor? component = stream.Descriptors.WithTag(DescriptorTags.DataComponent);

        return component is null ? EventMessagesOnly(stream) : Carousel(stream, component, defects);
    }

    private static DataBroadcastStream? EventMessagesOnly(ElementaryStream stream)
    {
        Descriptor? identifier = stream.Descriptors.WithTag(DescriptorTags.StreamIdentifier);

        if (identifier is null || identifier.Payload.IsEmpty)
        {
            return null;
        }

        int tag = identifier.Payload.Span[0];

        return tag is >= EntryComponentTag and <= LastDataComponentTag
            ? new DataBroadcastStream(stream.Pid, tag, null, CarriesCarousels: false)
            : null;
    }

    private static DataBroadcastStream? Carousel(ElementaryStream stream, Descriptor component, List<DataBroadcastStreamDefect> defects)
    {
        if (component.Payload.Length < DataComponentIdSize)
        {
            return Refuse(stream, DataBroadcastDefect.MalformedDataComponent, defects);
        }

        ReadOnlySpan<byte> payload = component.Payload.Span;

        if (((payload[0] << 8) | payload[1]) != BxmlDataComponentId)
        {
            return null;
        }

        Descriptor? identifier = stream.Descriptors.WithTag(DescriptorTags.StreamIdentifier);

        if (identifier is null || identifier.Payload.IsEmpty)
        {
            DataBroadcastDefect defect = identifier is null
                ? DataBroadcastDefect.MissingStreamIdentifier
                : DataBroadcastDefect.MalformedStreamIdentifier;

            return Refuse(stream, defect, defects);
        }

        ReadOnlySpan<byte> carried = payload[DataComponentIdSize..];
        BxmlInfo? info = BxmlInfo.Read(carried);

        if (info is null && !carried.IsEmpty)
        {
            defects.Add(new DataBroadcastStreamDefect(stream.Pid, DataBroadcastDefect.MalformedBxmlInfo));
        }

        return new DataBroadcastStream(stream.Pid, identifier.Payload.Span[0], info, CarriesCarousels: true);
    }

    private static DataBroadcastStream? Refuse(ElementaryStream stream, DataBroadcastDefect defect, List<DataBroadcastStreamDefect> defects)
    {
        defects.Add(new DataBroadcastStreamDefect(stream.Pid, defect));

        return null;
    }
}
