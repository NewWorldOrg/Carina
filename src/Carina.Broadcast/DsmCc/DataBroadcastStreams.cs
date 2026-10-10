using Carina.Broadcast.Descriptors;
using Carina.Broadcast.Tables;

namespace Carina.Broadcast.DsmCc;

public static class DataBroadcastStreams
{
    public const int DsmCcSectionsStreamType = 0x0D;

    public const int BxmlDataComponentId = 0x000C;

    public const int EntryComponentTag = 0x40;

    private const int DataComponentIdSize = 2;

    public static DataBroadcastService Find(ProgramMapTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        var found = new List<DataBroadcastStream>();
        var defects = new List<DataBroadcastStreamDefect>();

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
        Descriptor? component = stream.Descriptors.WithTag(DescriptorTags.DataComponent);

        if (stream.StreamType != DsmCcSectionsStreamType || component is null)
        {
            return null;
        }

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

        return new DataBroadcastStream(stream.Pid, identifier.Payload.Span[0], info);
    }

    private static DataBroadcastStream? Refuse(ElementaryStream stream, DataBroadcastDefect defect, List<DataBroadcastStreamDefect> defects)
    {
        defects.Add(new DataBroadcastStreamDefect(stream.Pid, defect));

        return null;
    }
}
