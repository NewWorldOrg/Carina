using System.Buffers.Binary;
using System.Text;

using Carina.Domain.DataBroadcast;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.DataBroadcast;

/// <summary>
/// The frames of the data broadcast side channel: the catalog in CBOR, a module with its resources, an event
/// message, and word that the service carries no data broadcast, each opened by the byte that says which.
/// </summary>
public static class DataBroadcastFrames
{
    public const byte CatalogKind = 0x01;

    public const byte ModuleKind = 0x02;

    public const byte EventKind = 0x03;

    public const byte AbsentKind = 0x04;

    public const int ModuleHeaderLength = sizeof(byte) + sizeof(byte) + sizeof(ushort) + sizeof(byte);

    private const int FiresAtOffset = sizeof(byte) + sizeof(ushort) + sizeof(ushort) + sizeof(byte) + sizeof(byte);

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly IReadOnlyDictionary<DataBroadcastResourceKind, (string MediaType, ResourceForm Form)> Representatives =
        new Dictionary<DataBroadcastResourceKind, (string MediaType, ResourceForm Form)>
        {
            [DataBroadcastResourceKind.Bml] = ("text/X-arib-bml", ResourceForm.Text),
            [DataBroadcastResourceKind.Css] = ("text/css", ResourceForm.Text),
            [DataBroadcastResourceKind.EcmaScript] = ("text/X-arib-ecmascript", ResourceForm.Text),
            [DataBroadcastResourceKind.Jpeg] = ("image/jpeg", ResourceForm.Binary),
            [DataBroadcastResourceKind.Png] = ("image/X-arib-png", ResourceForm.Binary),
            [DataBroadcastResourceKind.OtherBinary] = (CarouselReader.UnnamedMediaType, ResourceForm.Binary),
            [DataBroadcastResourceKind.UndecodedText] = ("text/plain", ResourceForm.UndecodedText),
        };

    private static readonly IReadOnlyDictionary<string, DataBroadcastResourceKind> Kinds =
        new Dictionary<string, DataBroadcastResourceKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["text/X-arib-bml"] = DataBroadcastResourceKind.Bml,
            ["text/css"] = DataBroadcastResourceKind.Css,
            ["text/X-arib-ecmascript"] = DataBroadcastResourceKind.EcmaScript,
            ["application/X-arib-ecmascript"] = DataBroadcastResourceKind.EcmaScript,
            ["image/jpeg"] = DataBroadcastResourceKind.Jpeg,
            ["image/X-arib-png"] = DataBroadcastResourceKind.Png,
            ["image/png"] = DataBroadcastResourceKind.Png,
        };

    public static LiveFrame Catalog(CarouselCatalog catalog, long at)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return Frame(at, [CatalogKind, .. CatalogCbor(catalog)]);
    }

    public static LiveFrame Module(ModuleVersion module, long at)
    {
        ArgumentNullException.ThrowIfNull(module);

        return Frame(at, ModulePayload(module));
    }

    public static LiveFrame Event(EventMessage message, long at)
    {
        ArgumentNullException.ThrowIfNull(message);

        byte[] payload = new byte[message.Bytes];

        WriteEvent(message, (long)OnTheWire(message.FiresAt), payload);

        return Frame(at, payload);
    }

    /// <summary>
    /// An event message as the side channel carries it, opened by its kind, with the moment it fires written as
    /// <paramref name="firesAt"/>, into <paramref name="into"/>, answering how many bytes that took.
    /// </summary>
    public static int WriteEvent(EventMessage message, long firesAt, Span<byte> into)
    {
        ArgumentNullException.ThrowIfNull(message);

        into[0] = EventKind;
        BinaryPrimitives.WriteUInt16BigEndian(into[1..], (ushort)message.Group);
        BinaryPrimitives.WriteUInt16BigEndian(into[3..], (ushort)message.Id);
        into[5] = (byte)message.MessageType;
        into[6] = (byte)message.Timing;
        BinaryPrimitives.WriteInt64BigEndian(into[FiresAtOffset..], firesAt);
        BinaryPrimitives.WriteUInt16BigEndian(into[15..], (ushort)message.PrivateData.Length);
        message.PrivateData.Span.CopyTo(into[EventMessage.FramingBytes..]);

        return EventMessage.FramingBytes + message.PrivateData.Length;
    }

    public static LiveFrame Absent(long at) => Frame(at, [AbsentKind]);

    /// <summary>
    /// The frame told on a clock <paramref name="by"/> ticks behind: its moment, and the moment an event message
    /// fires, moved back by that much and held at the clock's start.
    /// </summary>
    public static LiveFrame Shifted(LiveFrame frame, long by)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentOutOfRangeException.ThrowIfNegative(by);

        if (by is 0)
        {
            return frame;
        }

        ReadOnlyMemory<byte> payload = frame.Payload;

        if (!payload.IsEmpty && payload.Span[0] == EventKind && payload.Length >= EventMessage.FramingBytes)
        {
            byte[] moved = payload.ToArray();
            long firesAt = (long)BinaryPrimitives.ReadUInt64BigEndian(moved.AsSpan(FiresAtOffset));

            BinaryPrimitives.WriteUInt64BigEndian(moved.AsSpan(FiresAtOffset), OnTheWire(firesAt - by));
            payload = moved;
        }

        return new LiveFrame(frame.Channel, LivePts.Of(OnTheWire((long)frame.Pts.Value - by)), payload);
    }

    /// <summary>
    /// A module as the side channel carries it: its tag, id and version, then each resource with its path, its
    /// kind and its bytes, to the end.
    /// </summary>
    public static byte[] ModulePayload(ModuleVersion module)
    {
        ArgumentNullException.ThrowIfNull(module);

        byte[] payload = new byte[ModuleHeaderLength + module.Resources.Sum(resource => resource.Bytes)];
        Span<byte> written = payload;

        written[0] = ModuleKind;
        written[1] = (byte)module.Tag;
        BinaryPrimitives.WriteUInt16BigEndian(written[2..], (ushort)module.ModuleId);
        written[4] = (byte)module.Version;
        WriteResources(module.Resources, written[ModuleHeaderLength..]);

        return payload;
    }

    /// <summary>
    /// Each resource with its path, its kind and its bytes, one after another, into <paramref name="into"/>,
    /// answering how many bytes that took.
    /// </summary>
    public static int WriteResources(IReadOnlyList<CarouselResource> resources, Span<byte> into)
    {
        ArgumentNullException.ThrowIfNull(resources);

        int at = 0;

        foreach (CarouselResource resource in resources)
        {
            at += Write(resource, into[at..]);
        }

        return at;
    }

    /// <summary>
    /// The resources written one after another to the end of <paramref name="bytes"/>, each with the media type and
    /// the form its kind stands for, or null when the bytes are not a list of at least one resource.
    /// </summary>
    public static IReadOnlyList<CarouselResource>? ReadResources(ReadOnlyMemory<byte> bytes)
    {
        List<CarouselResource> resources = [];

        while (!bytes.IsEmpty)
        {
            if (Resource(bytes.Span) is not { } read)
            {
                return null;
            }

            (string mediaType, ResourceForm form) = Representatives[read.Kind];
            resources.Add(new CarouselResource(read.Path, mediaType, form, bytes.Slice(read.BodyAt, read.BodyLength)));
            bytes = bytes[(read.BodyAt + read.BodyLength)..];
        }

        return resources.Count is 0 ? null : resources;
    }

    /// <summary>
    /// The media type a resource of <paramref name="kind"/> is read back as.
    /// </summary>
    public static string MediaTypeOf(DataBroadcastResourceKind kind) => Representatives[kind].MediaType;

    public static DataBroadcastResourceKind KindOf(CarouselResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (resource.Form is ResourceForm.UndecodedText)
        {
            return DataBroadcastResourceKind.UndecodedText;
        }

        return Kinds.TryGetValue(resource.MediaType, out DataBroadcastResourceKind kind) ? kind : DataBroadcastResourceKind.OtherBinary;
    }

    public static byte[] CatalogCbor(CarouselCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        CborBytes cbor = new CborBytes()
            .Map(5)
            .Text("service").Unsigned((ulong)catalog.Service.Value)
            .Text("entryTag").Unsigned((ulong)catalog.EntryTag)
            .Text("autoStart").Boolean(catalog.AutoStart)
            .Text("startup").Text(catalog.StartupDocument)
            .Text("carousels").Array(catalog.Carousels.Count);

        foreach (CatalogCarousel carousel in catalog.Carousels)
        {
            cbor.Map(3)
                .Text("tag").Unsigned((ulong)carousel.Tag)
                .Text("downloadId").Unsigned(carousel.DownloadId)
                .Text("modules").Array(carousel.Modules.Count);

            foreach (CatalogModule module in carousel.Modules)
            {
                Write(module, cbor);
            }
        }

        return cbor.ToArray();
    }

    private static void Write(CatalogModule module, CborBytes cbor)
    {
        cbor.Map(4)
            .Text("id").Unsigned((ulong)module.Id)
            .Text("version").Unsigned((ulong)module.Version)
            .Text("size").Unsigned((ulong)module.Size)
            .Text("resources").Array(module.Resources.Count);

        foreach (CatalogResource resource in module.Resources)
        {
            cbor.Map(2).Text("path").Text(resource.Path).Text("type").Text(resource.MediaType);
        }
    }

    private static int Write(CarouselResource resource, Span<byte> into)
    {
        int pathLength = Encoding.UTF8.GetBytes(resource.Path, into[sizeof(ushort)..]);
        int at = sizeof(ushort) + pathLength;

        BinaryPrimitives.WriteUInt16BigEndian(into, (ushort)pathLength);
        into[at] = (byte)KindOf(resource);
        BinaryPrimitives.WriteUInt32BigEndian(into[(at + sizeof(byte))..], (uint)resource.Body.Length);
        at += sizeof(byte) + sizeof(uint);
        resource.Body.Span.CopyTo(into[at..]);

        return at + resource.Body.Length;
    }

    private static ResourceRead? Resource(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < CarouselResource.FramingBytes)
        {
            return null;
        }

        int pathLength = BinaryPrimitives.ReadUInt16BigEndian(bytes);
        int bodyAt = CarouselResource.FramingBytes + pathLength;

        if (pathLength is 0 || bytes.Length < bodyAt)
        {
            return null;
        }

        DataBroadcastResourceKind kind = (DataBroadcastResourceKind)bytes[sizeof(ushort) + pathLength];
        uint bodyLength = BinaryPrimitives.ReadUInt32BigEndian(bytes[(sizeof(ushort) + pathLength + sizeof(byte))..]);

        if (!Enum.IsDefined(kind) || bodyLength > (uint)(bytes.Length - bodyAt))
        {
            return null;
        }

        try
        {
            return new ResourceRead(Utf8.GetString(bytes.Slice(sizeof(ushort), pathLength)), kind, bodyAt, (int)bodyLength);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static LiveFrame Frame(long at, byte[] payload)
        => new(LiveChannel.DataBroadcast, LivePts.Of(OnTheWire(at)), payload);

    private static ulong OnTheWire(long at) => at < 0 ? 0UL : (ulong)at;

    private sealed record ResourceRead(string Path, DataBroadcastResourceKind Kind, int BodyAt, int BodyLength);
}
