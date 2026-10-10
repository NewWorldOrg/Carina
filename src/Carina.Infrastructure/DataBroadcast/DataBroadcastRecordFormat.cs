using System.Buffers.Binary;

using Carina.Domain.DataBroadcast;

namespace Carina.Infrastructure.DataBroadcast;

/// <summary>
/// How a <see cref="DataBroadcastRecord"/> is laid out on disk: a header naming the format, where the
/// recording's clock begins, the carousel it is entered from, a byte marking whether versions were left out and
/// whether the broadcaster asks for it to open by itself, and the count of carousels; then each carousel as its tag, its download id and its versions, each with its id, version, the
/// moments it was first and last seen and its resources as the side channel lists them; then the count of event
/// messages and each as the side channel carries it. Every number is big-endian, and every moment is signed.
/// </summary>
public static class DataBroadcastRecordFormat
{
    public const ushort FormatVersion = 2;

    private const ushort FirstVersion = 1;

    private const byte IncompleteMark = 0x01;

    private const byte AutoStartMark = 0x02;

    public const int HeaderLength = DataBroadcastRecord.HeaderBytes;

    private const int MagicLength = 8;

    private const int VersionAt = MagicLength;

    private const int StartsAtAt = VersionAt + sizeof(ushort);

    private const int EntryTagAt = StartsAtAt + sizeof(long);

    private const int MarksAt = EntryTagAt + sizeof(byte);

    private const int CarouselCountAt = MarksAt + sizeof(byte);

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
        head[MarksAt] = (byte)((record.Incomplete ? IncompleteMark : 0) | (record.AutoStart ? AutoStartMark : 0));
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
    /// Whether the head of a file is the head of a record this format wrote: its magic, a version it reads, marks
    /// that version names, and as many bytes as a header takes. A record of the first version marks only whether
    /// it is incomplete.
    /// </summary>
    public static bool Heads(ReadOnlySpan<byte> head)
        => head.Length >= HeaderLength
           && head[..MagicLength].SequenceEqual(Magic)
           && BinaryPrimitives.ReadUInt16BigEndian(head[VersionAt..]) switch
           {
               FirstVersion => head[MarksAt] <= IncompleteMark,
               FormatVersion => head[MarksAt] <= (IncompleteMark | AutoStartMark),
               _ => false,
           };

    /// <summary>
    /// Reads a record back, or answers null when the bytes are not one this format wrote.
    /// </summary>
    public static DataBroadcastRecord? Read(ReadOnlyMemory<byte> bytes)
    {
        ReadOnlySpan<byte> head = bytes.Span;

        if (!Heads(head))
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

    /// <summary>
    /// Finds one version in a record being read, stepping over every other version by its length without reading it,
    /// or answers null when the bytes are not a record this format wrote or the record does not hold that version.
    /// </summary>
    public static async Task<ModuleVersion?> FindAsync(Stream reading, ModuleVersionKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reading);

        byte[] head = new byte[HeaderLength];

        if (!await FilledAsync(reading, head, cancellationToken) || !Heads(head))
        {
            return null;
        }

        int carousels = BinaryPrimitives.ReadUInt16BigEndian(head.AsSpan(CarouselCountAt));
        byte[] carouselHead = new byte[RecordedCarousel.HeaderBytes];

        for (int carousel = 0; carousel < carousels; carousel++)
        {
            if (!await FilledAsync(reading, carouselHead, cancellationToken))
            {
                return null;
            }

            int tag = carouselHead[0];
            uint downloadId = BinaryPrimitives.ReadUInt32BigEndian(carouselHead.AsSpan(1));
            int versions = BinaryPrimitives.ReadUInt16BigEndian(carouselHead.AsSpan(5));

            if (tag == key.Tag && downloadId == key.DownloadId)
            {
                return await FindInCarouselAsync(reading, tag, versions, key, cancellationToken);
            }

            if (!await PassedAsync(reading, versions, cancellationToken))
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads a record being read without the resources of its versions, stepping over each by its length, or answers
    /// null when the bytes are not a record this format wrote. The resources themselves are not looked at.
    /// </summary>
    public static async Task<DataBroadcastOutline?> OutlineAsync(Stream reading, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reading);

        byte[] head = new byte[HeaderLength];

        if (!await FilledAsync(reading, head, cancellationToken) || !Heads(head))
        {
            return null;
        }

        try
        {
            return await OutlinedAsync(reading, head, cancellationToken);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static async Task<DataBroadcastOutline?> OutlinedAsync(Stream reading, byte[] head, CancellationToken cancellationToken)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(head.AsSpan(CarouselCountAt));
        List<OutlinedCarousel> carousels = [];
        byte[] carouselHead = new byte[RecordedCarousel.HeaderBytes];

        for (int carousel = 0; carousel < count; carousel++)
        {
            if (!await FilledAsync(reading, carouselHead, cancellationToken)
                || await OutlinedVersionsAsync(reading, BinaryPrimitives.ReadUInt16BigEndian(carouselHead.AsSpan(5)), cancellationToken) is not { } versions)
            {
                return null;
            }

            carousels.Add(new OutlinedCarousel(carouselHead[0], BinaryPrimitives.ReadUInt32BigEndian(carouselHead.AsSpan(1)), versions));
        }

        long left = reading.Length - reading.Position;

        if (left > int.MaxValue)
        {
            return null;
        }

        byte[] rest = new byte[left];

        if (!await FilledAsync(reading, rest, cancellationToken))
        {
            return null;
        }

        ReadOnlyMemory<byte> at = rest;

        if (Events(ref at) is not { } events || !at.IsEmpty)
        {
            return null;
        }

        return new DataBroadcastOutline(
            BinaryPrimitives.ReadInt64BigEndian(head.AsSpan(StartsAtAt)),
            head[EntryTagAt],
            carousels,
            events,
            (head[MarksAt] & IncompleteMark) != 0,
            (head[MarksAt] & AutoStartMark) != 0);
    }

    private static async Task<List<OutlinedVersion>?> OutlinedVersionsAsync(Stream reading, int count, CancellationToken cancellationToken)
    {
        List<OutlinedVersion> versions = [];
        byte[] versionHead = new byte[ModuleVersion.HeaderBytes];

        for (int read = 0; read < count; read++)
        {
            if (!await FilledAsync(reading, versionHead, cancellationToken))
            {
                return null;
            }

            uint entity = BinaryPrimitives.ReadUInt32BigEndian(versionHead.AsSpan(19));

            if (!Stepped(reading, entity))
            {
                return null;
            }

            versions.Add(new OutlinedVersion(
                BinaryPrimitives.ReadUInt16BigEndian(versionHead),
                versionHead[2],
                BinaryPrimitives.ReadInt64BigEndian(versionHead.AsSpan(3)),
                BinaryPrimitives.ReadInt64BigEndian(versionHead.AsSpan(11)),
                entity));
        }

        return versions;
    }

    private static async Task<ModuleVersion?> FindInCarouselAsync(
        Stream reading,
        int tag,
        int versions,
        ModuleVersionKey key,
        CancellationToken cancellationToken)
    {
        byte[] versionHead = new byte[ModuleVersion.HeaderBytes];

        for (int read = 0; read < versions; read++)
        {
            if (!await FilledAsync(reading, versionHead, cancellationToken))
            {
                return null;
            }

            uint entity = BinaryPrimitives.ReadUInt32BigEndian(versionHead.AsSpan(19));

            if (BinaryPrimitives.ReadUInt16BigEndian(versionHead) != key.ModuleId || versionHead[2] != key.Version)
            {
                if (!Stepped(reading, entity))
                {
                    return null;
                }

                continue;
            }

            if (entity > reading.Length - reading.Position)
            {
                return null;
            }

            byte[] whole = new byte[ModuleVersion.HeaderBytes + entity];

            versionHead.CopyTo(whole, 0);

            return await FilledAsync(reading, whole.AsMemory(ModuleVersion.HeaderBytes), cancellationToken)
                ? Found(tag, whole)
                : null;
        }

        return null;
    }

    private static ModuleVersion? Found(int tag, byte[] whole)
    {
        ReadOnlyMemory<byte> at = whole;

        try
        {
            return Version(tag, ref at);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static async Task<bool> PassedAsync(Stream reading, int versions, CancellationToken cancellationToken)
    {
        byte[] versionHead = new byte[ModuleVersion.HeaderBytes];

        for (int read = 0; read < versions; read++)
        {
            if (!await FilledAsync(reading, versionHead, cancellationToken)
                || !Stepped(reading, BinaryPrimitives.ReadUInt32BigEndian(versionHead.AsSpan(19))))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Stepped(Stream reading, uint entity)
    {
        if (entity > reading.Length - reading.Position)
        {
            return false;
        }

        reading.Seek(entity, SeekOrigin.Current);

        return true;
    }

    private static async Task<bool> FilledAsync(Stream reading, Memory<byte> into, CancellationToken cancellationToken)
        => await reading.ReadAtLeastAsync(into, into.Length, throwOnEndOfStream: false, cancellationToken) == into.Length;

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
            (head[MarksAt] & IncompleteMark) != 0,
            (head[MarksAt] & AutoStartMark) != 0);
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
