using System.Text;

using Carina.Domain.Channels;
using Carina.Domain.DataBroadcast;
using Carina.Domain.Streaming;
using Carina.Infrastructure.DataBroadcast;

namespace Carina.Infrastructure.Tests.DataBroadcast;

public sealed class DataBroadcastFramesTests
{
    private const long At = 123_456_789L;

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47];

    [Fact(DisplayName = "BR-BD-004: a module is written with its tag, id and version and read back resource by resource")]
    public void AModuleIsWrittenAndReadBackResourceByResource()
    {
        ModuleVersion module = new(0x40, 0x0102, 7, At, At, [
            new CarouselResource("startup.bml", "text/X-arib-bml", ResourceForm.Text, Encoding.UTF8.GetBytes("<bml>天</bml>")),
            new CarouselResource("画像/logo.png", "image/X-arib-png", ResourceForm.Binary, Png),
        ]);

        LiveFrame frame = DataBroadcastFrames.Module(module, At);
        ModuleRead read = SideChannelReading.Module(frame);

        Assert.Equal(LiveChannel.DataBroadcast, frame.Channel);
        Assert.Equal((ulong)At, frame.Pts.Value);
        Assert.Equal((0x40, 0x0102, 7), (read.Tag, read.ModuleId, read.Version));
        Assert.Equal(["startup.bml", "画像/logo.png"], read.Resources.Select(resource => resource.Path));
        Assert.Equal([DataBroadcastResourceKind.Bml, DataBroadcastResourceKind.Png], read.Resources.Select(resource => resource.Kind));
        Assert.Equal("<bml>天</bml>", Encoding.UTF8.GetString(read.Resources[0].Body));
        Assert.Equal(Png, read.Resources[1].Body);
        Assert.Equal(frame.Payload.ToArray(), DataBroadcastFrames.ModulePayload(module));
    }

    [Theory(DisplayName = "BR-BD-004: a resource's kind is told from its media type, and text left undecoded says so")]
    [InlineData("text/X-arib-bml", ResourceForm.Text, DataBroadcastResourceKind.Bml)]
    [InlineData("text/css", ResourceForm.Text, DataBroadcastResourceKind.Css)]
    [InlineData("text/X-arib-ecmascript", ResourceForm.Text, DataBroadcastResourceKind.EcmaScript)]
    [InlineData("application/X-arib-ecmascript", ResourceForm.Text, DataBroadcastResourceKind.EcmaScript)]
    [InlineData("image/jpeg", ResourceForm.Binary, DataBroadcastResourceKind.Jpeg)]
    [InlineData("image/X-arib-png", ResourceForm.Binary, DataBroadcastResourceKind.Png)]
    [InlineData("image/X-arib-mng", ResourceForm.Binary, DataBroadcastResourceKind.OtherBinary)]
    [InlineData("text/X-arib-bml", ResourceForm.UndecodedText, DataBroadcastResourceKind.UndecodedText)]
    public void AResourcesKindIsToldFromItsMediaType(string mediaType, ResourceForm form, DataBroadcastResourceKind kind)
        => Assert.Equal(kind, DataBroadcastFrames.KindOf(new CarouselResource("a", mediaType, form, Png)));

    [Fact(DisplayName = "BR-BD-004: an event message is written with its group, id, type, timing, moment and private data")]
    public void AnEventMessageIsWrittenAndReadBack()
    {
        EventMessage message = new(0xABC, 0x1234, 0x05, EventTiming.Npt, (1L << 33) + 90_000, new byte[] { 0x01, 0x02, 0x03 });

        LiveFrame frame = DataBroadcastFrames.Event(message, At);
        EventRead read = SideChannelReading.Event(frame);

        Assert.Equal((0xABC, 0x1234, 0x05, (int)EventTiming.Npt), (read.Group, read.Id, read.MessageType, read.Timing));
        Assert.Equal((1UL << 33) + 90_000, read.FiresAt);
        Assert.Equal([0x01, 0x02, 0x03], read.PrivateData);
        Assert.Equal(message.Bytes, frame.Payload.Length);
    }

    [Fact(DisplayName = "BR-BD-004: word that there is no data broadcast is the kind byte alone")]
    public void WordThatThereIsNoDataBroadcastIsTheKindByteAlone()
    {
        LiveFrame frame = DataBroadcastFrames.Absent(At);

        Assert.Equal([DataBroadcastFrames.AbsentKind], frame.Payload.ToArray());
        Assert.Equal((ulong)At, frame.Pts.Value);
    }

    [Fact(DisplayName = "BR-BD-004: the catalog is CBOR keyed by the names the player reads")]
    public void TheCatalogIsCborKeyedByTheNamesThePlayerReads()
    {
        CarouselCatalog catalog = new(new ServiceId(1024), 0x40, true, [
            new CatalogCarousel(0x40, 0x0000_0001, [
                new CatalogModule(0, 3, 300, true, [new CatalogResource("startup.bml", "text/X-arib-bml")]),
                new CatalogModule(1, 2, 70_000, false, []),
            ]),
            new CatalogCarousel(0x50, 0xFFFF_FFFF, []),
        ]);

        IReadOnlyDictionary<string, object> read = SideChannelReading.Catalog(DataBroadcastFrames.Catalog(catalog, At));

        Assert.Equal(["service", "entryTag", "autoStart", "startup", "carousels"], read.Keys);
        Assert.Equal(1024UL, read["service"]);
        Assert.Equal(0x40UL, read["entryTag"]);
        Assert.Equal(true, read["autoStart"]);
        Assert.Equal("/40/0000/startup.bml", read["startup"]);
        List<object> carousels = Assert.IsType<List<object>>(read["carousels"]);
        Dictionary<string, object> entry = Assert.IsType<Dictionary<string, object>>(carousels[0]);
        Assert.Equal(["tag", "downloadId", "modules"], entry.Keys);
        Assert.Equal(0xFFFF_FFFFUL, Assert.IsType<Dictionary<string, object>>(carousels[1])["downloadId"]);
        List<object> modules = Assert.IsType<List<object>>(entry["modules"]);
        Dictionary<string, object> startup = Assert.IsType<Dictionary<string, object>>(modules[0]);
        Assert.Equal(["id", "version", "size", "resources"], startup.Keys);
        Assert.Equal((0UL, 3UL, 300UL), ((ulong)startup["id"], (ulong)startup["version"], (ulong)startup["size"]));
        Dictionary<string, object> resource = Assert.IsType<Dictionary<string, object>>(Assert.Single(Assert.IsType<List<object>>(startup["resources"])));
        Assert.Equal(("startup.bml", "text/X-arib-bml"), ((string)resource["path"], (string)resource["type"]));
        Assert.Empty(Assert.IsType<List<object>>(Assert.IsType<Dictionary<string, object>>(modules[1])["resources"]));
        Assert.Equal(70_000UL, Assert.IsType<Dictionary<string, object>>(modules[1])["size"]);
    }

    [Fact(DisplayName = "BR-BD-004: the catalog's CBOR is written byte for byte as RFC 8949 lays it out")]
    public void TheCatalogsCborIsWrittenByteForByte()
    {
        CarouselCatalog catalog = new(new ServiceId(24), 0x40, false, []);

        byte[] expected =
        [
            0xA5,
            0x67, .. "service"u8, 0x18, 0x18,
            0x68, .. "entryTag"u8, 0x18, 0x40,
            0x69, .. "autoStart"u8, 0xF4,
            0x67, .. "startup"u8, 0x74, .. "/40/0000/startup.bml"u8,
            0x69, .. "carousels"u8, 0x80,
        ];

        Assert.Equal(expected, DataBroadcastFrames.CatalogCbor(catalog));
    }

    [Fact]
    public void AMomentBeforeTheClockBeganIsSentAsItsStart()
        => Assert.Equal(0UL, DataBroadcastFrames.Absent(-1).Pts.Value);
}
