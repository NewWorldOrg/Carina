using System.Buffers.Binary;
using System.Text;

using Carina.Domain.Streaming;
using Carina.Infrastructure.DataBroadcast;

namespace Carina.Infrastructure.Tests.DataBroadcast;

/// <summary>
/// Reads the frames of the data broadcast side channel back the way a player would, with a CBOR reader of its
/// own, so that what is written is checked against the format and not against the writer.
/// </summary>
internal static class SideChannelReading
{
    public static byte KindOf(LiveFrame frame) => frame.Payload.Span[0];

    public static IReadOnlyDictionary<string, object> Catalog(LiveFrame frame)
    {
        Assert.Equal(LiveChannel.DataBroadcast, frame.Channel);
        Assert.Equal(DataBroadcastFrames.CatalogKind, KindOf(frame));

        int at = 1;
        object read = Cbor(frame.Payload.Span, ref at);

        Assert.Equal(frame.Payload.Length, at);

        return Assert.IsAssignableFrom<IReadOnlyDictionary<string, object>>(read);
    }

    public static ModuleRead Module(LiveFrame frame)
    {
        Assert.Equal(DataBroadcastFrames.ModuleKind, KindOf(frame));

        ReadOnlySpan<byte> payload = frame.Payload.Span;
        List<ResourceRead> resources = [];
        int at = 5;

        while (at < payload.Length)
        {
            int pathLength = BinaryPrimitives.ReadUInt16BigEndian(payload[at..]);
            string path = Encoding.UTF8.GetString(payload.Slice(at + 2, pathLength));
            int kind = payload[at + 2 + pathLength];
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(payload[(at + 3 + pathLength)..]);
            int body = at + 7 + pathLength;

            resources.Add(new ResourceRead(path, (DataBroadcastResourceKind)kind, payload.Slice(body, length).ToArray()));
            at = body + length;
        }

        Assert.Equal(payload.Length, at);

        return new ModuleRead(payload[1], BinaryPrimitives.ReadUInt16BigEndian(payload[2..]), payload[4], resources);
    }

    public static EventRead Event(LiveFrame frame)
    {
        Assert.Equal(DataBroadcastFrames.EventKind, KindOf(frame));

        ReadOnlySpan<byte> payload = frame.Payload.Span;
        int length = BinaryPrimitives.ReadUInt16BigEndian(payload[15..]);

        Assert.Equal(17 + length, payload.Length);

        return new EventRead(
            BinaryPrimitives.ReadUInt16BigEndian(payload[1..]),
            BinaryPrimitives.ReadUInt16BigEndian(payload[3..]),
            payload[5],
            payload[6],
            BinaryPrimitives.ReadUInt64BigEndian(payload[7..]),
            payload[17..].ToArray());
    }

    private static object Cbor(ReadOnlySpan<byte> bytes, ref int at)
    {
        byte initial = bytes[at++];
        int major = initial >> 5;
        int additional = initial & 0b1_1111;

        if (major is 7)
        {
            return additional switch
            {
                20 => false,
                21 => true,
                _ => throw new InvalidDataException($"Simple value {additional} is not one the catalog uses."),
            };
        }

        ulong argument = Argument(bytes, additional, ref at);

        return major switch
        {
            0 => argument,
            3 => Text(bytes, (int)argument, ref at),
            4 => Items(bytes, (int)argument, ref at),
            5 => Pairs(bytes, (int)argument, ref at),
            _ => throw new InvalidDataException($"Major type {major} is not one the catalog uses."),
        };
    }

    private static ulong Argument(ReadOnlySpan<byte> bytes, int additional, ref int at)
    {
        int length = additional switch
        {
            < 24 => 0,
            24 => 1,
            25 => 2,
            26 => 4,
            27 => 8,
            _ => throw new InvalidDataException($"Additional information {additional} is not a definite length."),
        };
        ulong argument = length is 0 ? (ulong)additional : 0UL;

        for (int index = 0; index < length; index++)
        {
            argument = (argument << 8) | bytes[at++];
        }

        return argument;
    }

    private static string Text(ReadOnlySpan<byte> bytes, int length, ref int at)
    {
        string text = Encoding.UTF8.GetString(bytes.Slice(at, length));

        at += length;

        return text;
    }

    private static List<object> Items(ReadOnlySpan<byte> bytes, int count, ref int at)
    {
        List<object> items = [];

        for (int index = 0; index < count; index++)
        {
            items.Add(Cbor(bytes, ref at));
        }

        return items;
    }

    private static Dictionary<string, object> Pairs(ReadOnlySpan<byte> bytes, int count, ref int at)
    {
        Dictionary<string, object> pairs = [];

        for (int index = 0; index < count; index++)
        {
            string key = Assert.IsType<string>(Cbor(bytes, ref at));

            pairs.Add(key, Cbor(bytes, ref at));
        }

        return pairs;
    }
}

internal sealed record ModuleRead(int Tag, int ModuleId, int Version, IReadOnlyList<ResourceRead> Resources);

internal sealed record ResourceRead(string Path, DataBroadcastResourceKind Kind, byte[] Body);

internal sealed record EventRead(int Group, int Id, int MessageType, int Timing, ulong FiresAt, byte[] PrivateData);
