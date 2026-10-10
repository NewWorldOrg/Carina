using System.Buffers.Binary;

using Carina.Domain.DataBroadcast;

namespace Carina.Infrastructure.DataBroadcast;

/// <summary>
/// How a <see cref="DataBroadcastRecord"/> is laid out on disk: a header naming the format, where the
/// recording's clock begins, the carousel it is entered from, whether versions were left out, and the count of
/// carousels; then each carousel as its tag, its download id and its versions, each with its id, version, the
/// moments it was first and last seen and its resources as the side channel lists them; then the count of event
/// messages and each as the side channel carries it. Every number is big-endian, and every moment is signed.
/// </summary>
public static class DataBroadcastRecordFormat
{
    public const ushort FormatVersion = 1;

    public const int HeaderLength = DataBroadcastRecord.HeaderBytes;

    private const int MagicLength = 8;

    private const int VersionAt = MagicLength;

    private const int StartsAtAt = VersionAt + sizeof(ushort);

    private const int EntryTagAt = StartsAtAt + sizeof(long);

    private const int IncompleteAt = EntryTagAt + sizeof(byte);

    private const int CarouselCountAt = IncompleteAt + sizeof(byte);

    private static ReadOnlySpan<byte> Magic => "CARINADB"u8;

    public static byte[] Written(DataBroadcastRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        using MemoryStream written = new(checked((int)record.Bytes));

        Write(record, written);

        return written.ToArray();
    }

    /// <exception cref="ArgumentException">A carousel holds more versions than a count of two bytes tells.</exception>
    public static void Write(DataBroadcastRecord record, Stream into)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(into);

        if (record.Carousels.Count > ushort.MaxValue || record.Carousels.Any(carousel => carousel.Versions.Count > ushort.MaxValue))
        {
            throw new ArgumentException("A record tells its carousels, and the versions of each, in two bytes.", nameof(record));
        }

        Span<byte> head = stackalloc byte[HeaderLength];

        Magic.CopyTo(head);
        BinaryPrimitives.WriteUInt16BigEndian(head[VersionAt..], FormatVersion);
        BinaryPrimitives.WriteInt64BigEndian(head[StartsAtAt..], record.StartsAt);
        head[EntryTagAt] = (byte)record.EntryTag;
        head[IncompleteAt] = record.Incomplete ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt16BigEndian(head[CarouselCountAt..], (ushort)record.Carousels.Count);
        into.Write(head);

        foreach (RecordedCarousel carousel in record.Carousels)
        {
            Write(carousel, into);
        }

        Span<byte> count = stackalloc byte[DataBroadcastRecord.EventCountBytes];

        BinaryPrimitives.WriteUInt32BigEndian(count, (uint)record.Events.Count);
        into.Write(count);

        foreach (EventMessage message in record.Events)
        {
            byte[] written = new byte[message.Bytes];

            DataBroadcastFrames.WriteEvent(message, message.FiresAt, written);
            into.Write(written);
        }
    }

    /// <summary>
    /// Reads a record back, or answers null when the bytes are not one this format wrote.
    /// </summary>
    public static DataBroadcastRecord? Read(ReadOnlyMemory<byte> bytes)
    {
        ReadOnlySpan<byte> head = bytes.Span;

        if (head.Length < HeaderLength
            || !head[..MagicLength].SequenceEqual(Magic)
            || BinaryPrimitives.ReadUInt16BigEndian(head[VersionAt..]) != FormatVersion
            || head[IncompleteAt] > 1)
        {
            return null;
        }

        try
        {
            return Body(bytes, head);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static DataBroadcastRecord? Body(ReadOnlyMemory<byte> bytes, ReadOnlySpan<byte> head)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(head[CarouselCountAt..]);
        ReadOnlyMemory<byte> at = bytes[HeaderLength..];
        List<RecordedCarousel> carousels = new(Math.Min(count, at.Length / RecordedCarousel.HeaderBytes));

        for (int read = 0; read < count; read++)
        {
            if (Carousel(ref at) is not { } carousel)
            {
                return null;
            }

            carousels.Add(carousel);
        }

        if (Events(ref at) is not { } events || !at.IsEmpty)
        {
            return null;
        }

        return new DataBroadcastRecord(
            BinaryPrimitives.ReadInt64BigEndian(head[StartsAtAt..]),
            head[EntryTagAt],
            carousels,
            events,
            head[IncompleteAt] is 1);
    }

    private static void Write(RecordedCarousel carousel, Stream into)
    {
        Span<byte> head = stackalloc byte[RecordedCarousel.HeaderBytes];

        head[0] = (byte)carousel.Tag;
        BinaryPrimitives.WriteUInt32BigEndian(head[1..], carousel.DownloadId);
        BinaryPrimitives.WriteUInt16BigEndian(head[5..], (ushort)carousel.Versions.Count);
        into.Write(head);

        foreach (ModuleVersion version in carousel.Versions)
        {
            Write(version, into);
        }
    }

    private static void Write(ModuleVersion version, Stream into)
    {
        long entity = version.Bytes - ModuleVersion.HeaderBytes;
        byte[] written = new byte[version.Bytes];
        Span<byte> at = written;

        BinaryPrimitives.WriteUInt16BigEndian(at, (ushort)version.ModuleId);
        at[2] = (byte)version.Version;
        BinaryPrimitives.WriteInt64BigEndian(at[3..], version.FirstSeen);
        BinaryPrimitives.WriteInt64BigEndian(at[11..], version.LastSeen);
        BinaryPrimitives.WriteUInt32BigEndian(at[19..], (uint)entity);
        DataBroadcastFrames.WriteResources(version.Resources, at[ModuleVersion.HeaderBytes..]);
        into.Write(written);
    }

    private static RecordedCarousel? Carousel(ref ReadOnlyMemory<byte> at)
    {
        if (at.Length < RecordedCarousel.HeaderBytes)
        {
            return null;
        }

        ReadOnlySpan<byte> head = at.Span;
        int tag = head[0];
        uint downloadId = BinaryPrimitives.ReadUInt32BigEndian(head[1..]);
        int count = BinaryPrimitives.ReadUInt16BigEndian(head[5..]);

        at = at[RecordedCarousel.HeaderBytes..];

        List<ModuleVersion> versions = new(Math.Min(count, at.Length / ModuleVersion.HeaderBytes));

        for (int read = 0; read < count; read++)
        {
            if (Version(tag, ref at) is not { } version)
            {
                return null;
            }

            versions.Add(version);
        }

        return new RecordedCarousel(tag, downloadId, versions);
    }

    private static ModuleVersion? Version(int tag, ref ReadOnlyMemory<byte> at)
    {
        if (at.Length < ModuleVersion.HeaderBytes)
        {
            return null;
        }

        ReadOnlySpan<byte> head = at.Span;
        uint entity = BinaryPrimitives.ReadUInt32BigEndian(head[19..]);

        if (entity > (uint)(at.Length - ModuleVersion.HeaderBytes)
            || DataBroadcastFrames.ReadResources(at.Slice(ModuleVersion.HeaderBytes, (int)entity)) is not { } resources)
        {
            return null;
        }

        ModuleVersion version = new(
            tag,
            BinaryPrimitives.ReadUInt16BigEndian(head),
            head[2],
            BinaryPrimitives.ReadInt64BigEndian(head[3..]),
            BinaryPrimitives.ReadInt64BigEndian(head[11..]),
            resources);

        at = at[(ModuleVersion.HeaderBytes + (int)entity)..];

        return version;
    }

    private static List<EventMessage>? Events(ref ReadOnlyMemory<byte> at)
    {
        if (at.Length < DataBroadcastRecord.EventCountBytes)
        {
            return null;
        }

        uint count = BinaryPrimitives.ReadUInt32BigEndian(at.Span);

        at = at[DataBroadcastRecord.EventCountBytes..];

        List<EventMessage> events = new((int)Math.Min(count, (uint)(at.Length / EventMessage.FramingBytes)));

        for (uint read = 0; read < count; read++)
        {
            if (Event(ref at) is not { } message)
            {
                return null;
            }

            events.Add(message);
        }

        return events;
    }

    private static EventMessage? Event(ref ReadOnlyMemory<byte> at)
    {
        if (at.Length < EventMessage.FramingBytes || at.Span[0] != DataBroadcastFrames.EventKind)
        {
            return null;
        }

        ReadOnlySpan<byte> head = at.Span;
        int length = BinaryPrimitives.ReadUInt16BigEndian(head[15..]);

        if (at.Length - EventMessage.FramingBytes < length)
        {
            return null;
        }

        EventMessage message = new(
            BinaryPrimitives.ReadUInt16BigEndian(head[1..]),
            BinaryPrimitives.ReadUInt16BigEndian(head[3..]),
            head[5],
            (EventTiming)head[6],
            BinaryPrimitives.ReadInt64BigEndian(head[7..]),
            at.Slice(EventMessage.FramingBytes, length));

        at = at[(EventMessage.FramingBytes + length)..];

        return message;
    }
}
