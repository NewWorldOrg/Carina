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

        foreach (ElementaryStream stream in table.Streams)
        {
            if (Read(stream) is { } carried)
            {
                found.Add(carried);
            }
        }

        return new DataBroadcastService(found);
    }

    private static DataBroadcastStream? Read(ElementaryStream stream)
    {
        if (stream.StreamType != DsmCcSectionsStreamType)
        {
            return null;
        }

        Descriptor? identifier = stream.Descriptors.WithTag(DescriptorTags.StreamIdentifier);
        Descriptor? component = stream.Descriptors.WithTag(DescriptorTags.DataComponent);

        if (identifier is null || identifier.Payload.IsEmpty || component is null || component.Payload.Length < DataComponentIdSize)
        {
            return null;
        }

        ReadOnlySpan<byte> payload = component.Payload.Span;

        if (((payload[0] << 8) | payload[1]) != BxmlDataComponentId)
        {
            return null;
        }

        return new DataBroadcastStream(stream.Pid, identifier.Payload.Span[0], BxmlInfo.Read(payload[DataComponentIdSize..]));
    }
}
