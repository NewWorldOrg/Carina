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
        Span<byte> written = payload;

        written[0] = EventKind;
        BinaryPrimitives.WriteUInt16BigEndian(written[1..], (ushort)message.Group);
        BinaryPrimitives.WriteUInt16BigEndian(written[3..], (ushort)message.Id);
        written[5] = (byte)message.MessageType;
        written[6] = (byte)message.Timing;
        BinaryPrimitives.WriteUInt64BigEndian(written[7..], OnTheWire(message.FiresAt));
        BinaryPrimitives.WriteUInt16BigEndian(written[15..], (ushort)message.PrivateData.Length);
        message.PrivateData.Span.CopyTo(written[EventMessage.FramingBytes..]);

        return Frame(at, payload);
    }

    public static LiveFrame Absent(long at) => Frame(at, [AbsentKind]);

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
        int at = ModuleHeaderLength;

        foreach (CarouselResource resource in module.Resources)
        {
            at += Write(resource, written[at..]);
        }

        return payload;
    }

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

    private static LiveFrame Frame(long at, byte[] payload)
        => new(LiveChannel.DataBroadcast, LivePts.Of(OnTheWire(at)), payload);

    private static ulong OnTheWire(long at) => at < 0 ? 0UL : (ulong)at;
}
