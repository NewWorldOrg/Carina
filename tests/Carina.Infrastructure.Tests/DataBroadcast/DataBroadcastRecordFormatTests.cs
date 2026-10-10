using System.Text;

using Carina.Domain.DataBroadcast;
using Carina.Domain.Streaming;
using Carina.Infrastructure.DataBroadcast;

namespace Carina.Infrastructure.Tests.DataBroadcast;

public sealed class DataBroadcastRecordFormatTests
{
    private const int Entry = 0x40;

    private const int Other = 0x50;

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47];

    private static readonly byte[] VersionOne =
    [
        0x43, 0x41, 0x52, 0x49, 0x4E, 0x41, 0x44, 0x42,
        0x00, 0x01,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x5F, 0x90,
        0x40,
        0x01,
        0x00, 0x01,
        0x40, 0x00, 0x00, 0x00, 0x07, 0x00, 0x01,
        0x00, 0x00, 0x01,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0xBF, 0x20,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x1E, 0xB0,
        0x00, 0x00, 0x00, 0x0B,
        0x00, 0x01, 0x61, 0x01, 0x00, 0x00, 0x00, 0x03, 0x3C, 0x61, 0x3E,
        0x00, 0x00, 0x00, 0x01,
        0x03, 0x00, 0x01, 0x00, 0x02, 0x05, 0x01,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0xA9, 0x80,
        0x00, 0x02, 0xAA, 0xBB,
    ];

    private static readonly byte[] VersionTwo = Rewritten(VersionOne, (9, 0x02), (19, 0x03));

    [Fact(DisplayName = "BR-BD-005: a record is written and read back whole, and takes the bytes it says it takes")]
    public void ARecordIsWrittenAndReadBackWhole()
    {
        DataBroadcastRecord record = Gathered();

        byte[] written = DataBroadcastRecordFormat.Written(record);
        DataBroadcastRecord read = DataBroadcastRecordFormat.Read(written)!;

        Assert.Equal(record.Bytes, written.Length);
        Assert.Equal(written, DataBroadcastRecordFormat.Written(read));
        Assert.Equal((-90_000L, Entry, true, 3), (read.StartsAt, read.EntryTag, read.Incomplete, read.Modules));
        Assert.Equal([(Entry, 1u), (Other, 9u)], read.Carousels.Select(carousel => (carousel.Tag, carousel.DownloadId)));
        Assert.Equal(
            [(0, 1, -45_000L, 900_000L), (0, 2, 900_000L, 1_800_000L), (3, 1, 1_000_000L, 1_000_000L)],
            read.Carousels[0].Versions.Select(version => (version.ModuleId, version.Version, version.FirstSeen, version.LastSeen)));
        Assert.Equal("<bml>天気</bml>", Encoding.UTF8.GetString(read.Carousels[0].Versions[0].Resources[0].Body.Span));
        Assert.Equal(Png, read.Carousels[1].Versions[0].Resources[0].Body.ToArray());
        Assert.Equal([-9_000L, 450_000L], read.Events.Select(message => message.FiresAt));
        Assert.Equal([0x01, 0x02], read.Events[1].PrivateData.ToArray());
    }

    [Fact(DisplayName = "BR-BD-005: a record over its size is written without the oldest superseded version and read back marked incomplete")]
    public void ARecordOverItsSizeIsWrittenWithoutTheOldestSupersededVersionAndReadBackIncomplete()
    {
        DataBroadcastRecord whole = new(0, Entry, [.. Gathered().Carousels], [], false);
        ModuleVersion oldest = whole.Carousels[0].Versions.Single(version => version is { ModuleId: 3 });
        DataBroadcastRecord superseding = new(
            0,
            Entry,
            [
                whole.Carousels[1],
                new RecordedCarousel(Entry, 1, [.. whole.Carousels[0].Versions, new ModuleVersion(Entry, 3, 2, 1_500_000, 1_800_000, oldest.Resources)]),
            ],
            [],
            false);

        byte[] written = DataBroadcastRecordFormat.Written(superseding.Within(superseding.Bytes - 1));
        DataBroadcastRecord read = DataBroadcastRecordFormat.Read(written)!;

        Assert.True(read.Incomplete);
        Assert.Equal(superseding.Bytes - oldest.Bytes, written.Length);
        Assert.Equal(
            [(0, 1), (0, 2), (3, 2)],
            read.Carousels[0].Versions.Select(version => (version.ModuleId, version.Version)));
    }

    [Theory(DisplayName = "BR-BD-005: a resource read back from a record takes the media type and form its kind stands for")]
    [InlineData("text/X-arib-bml", ResourceForm.Text, "text/X-arib-bml", ResourceForm.Text)]
    [InlineData("text/css", ResourceForm.Text, "text/css", ResourceForm.Text)]
    [InlineData("application/X-arib-ecmascript", ResourceForm.Text, "text/X-arib-ecmascript", ResourceForm.Text)]
    [InlineData("image/jpeg", ResourceForm.Binary, "image/jpeg", ResourceForm.Binary)]
    [InlineData("image/png", ResourceForm.Binary, "image/X-arib-png", ResourceForm.Binary)]
    [InlineData("image/X-arib-mng", ResourceForm.Binary, "application/octet-stream", ResourceForm.Binary)]
    [InlineData("text/X-arib-bml", ResourceForm.UndecodedText, "text/plain", ResourceForm.UndecodedText)]
    public void AResourceReadBackTakesTheMediaTypeItsKindStandsFor(string written, ResourceForm form, string mediaType, ResourceForm readAs)
    {
        DataBroadcastRecord record = Single(new CarouselResource("a", written, form, Png));

        CarouselResource read = DataBroadcastRecordFormat.Read(DataBroadcastRecordFormat.Written(record))!.Carousels[0].Versions[0].Resources[0];

        Assert.Equal((mediaType, readAs), (read.MediaType, read.Form));
        Assert.Equal(DataBroadcastFrames.KindOf(record.Carousels[0].Versions[0].Resources[0]), DataBroadcastFrames.KindOf(read));
    }

    [Fact(DisplayName = "BR-BD-005: the entity of each version is the resource list of the side channel's module, so the module answer only puts its five bytes in front")]
    public void TheEntityIsTheResourceListOfTheSideChannelsModule()
    {
        DataBroadcastRecord record = Gathered();
        ModuleVersion version = record.Carousels[0].Versions[0];

        byte[] written = DataBroadcastRecordFormat.Written(record);
        int entityAt = DataBroadcastRecordFormat.HeaderLength + RecordedCarousel.HeaderBytes + ModuleVersion.HeaderBytes;
        byte[] payload = DataBroadcastFrames.ModulePayload(version);

        Assert.Equal(
            payload[DataBroadcastFrames.ModuleHeaderLength..],
            written[entityAt..(entityAt + payload.Length - DataBroadcastFrames.ModuleHeaderLength)]);
    }

    [Fact(DisplayName = "BR-BD-005: each event message of a record is the side channel's event message, its kind included")]
    public void EachEventMessageIsTheSideChannelsEventMessage()
    {
        EventMessage message = new(0xABC, 0x1234, 0x05, EventTiming.Npt, (1L << 33) + 90_000, new byte[] { 0x01, 0x02, 0x03 });
        DataBroadcastRecord record = new(0, Entry, [], [message], false);

        byte[] written = DataBroadcastRecordFormat.Written(record);
        LiveFrame frame = DataBroadcastFrames.Event(message, 0);

        Assert.Equal(frame.Payload.ToArray(), written[(DataBroadcastRecordFormat.HeaderLength + DataBroadcastRecord.EventCountBytes)..]);
    }

    [Fact(DisplayName = "BR-BV-004: a record of format version 1 written byte by byte is read")]
    public void ARecordOfVersionOneIsRead()
    {
        DataBroadcastRecord read = DataBroadcastRecordFormat.Read(VersionOne)!;

        Assert.Equal((90_000L, Entry, true), (read.StartsAt, read.EntryTag, read.Incomplete));
        RecordedCarousel carousel = Assert.Single(read.Carousels);
        ModuleVersion version = Assert.Single(carousel.Versions);
        CarouselResource resource = Assert.Single(version.Resources);
        EventMessage message = Assert.Single(read.Events);
        Assert.Equal((Entry, 7u), (carousel.Tag, carousel.DownloadId));
        Assert.Equal((0, 1, 180_000L, 270_000L), (version.ModuleId, version.Version, version.FirstSeen, version.LastSeen));
        Assert.Equal(("a", "text/X-arib-bml", ResourceForm.Text, "<a>"), (resource.Path, resource.MediaType, resource.Form, Encoding.ASCII.GetString(resource.Body.Span)));
        Assert.Equal((1, 2, 5, EventTiming.Immediate, 240_000L), (message.Group, message.Id, message.MessageType, message.Timing, message.FiresAt));
        Assert.Equal([0xAA, 0xBB], message.PrivateData.ToArray());
        Assert.False(read.AutoStart);
        Assert.Equal(Rewritten(VersionOne, (9, 0x02)), DataBroadcastRecordFormat.Written(read));
    }

    [Fact(DisplayName = "BR-BV-004: a record of format version 2 written byte by byte is read with whether it opens by itself")]
    public void ARecordOfVersionTwoIsReadWithWhetherItOpensByItself()
    {
        DataBroadcastRecord read = DataBroadcastRecordFormat.Read(VersionTwo)!;

        Assert.Equal((90_000L, Entry, true, true), (read.StartsAt, read.EntryTag, read.Incomplete, read.AutoStart));
        ModuleVersion version = Assert.Single(Assert.Single(read.Carousels).Versions);
        Assert.Equal((0, 1, 180_000L, 270_000L), (version.ModuleId, version.Version, version.FirstSeen, version.LastSeen));
        Assert.Equal(VersionTwo, DataBroadcastRecordFormat.Written(read));
    }

    [Theory(DisplayName = "BR-BD-005: whether a record opens by itself and whether it is incomplete are written apart and read back")]
    [InlineData(false, false, 0x00)]
    [InlineData(true, false, 0x01)]
    [InlineData(false, true, 0x02)]
    [InlineData(true, true, 0x03)]
    public void WhetherARecordOpensByItselfAndIsIncompleteAreWrittenApart(bool incomplete, bool autoStart, byte marks)
    {
        DataBroadcastRecord record = new(0, Entry, [], [], incomplete, autoStart);

        byte[] written = DataBroadcastRecordFormat.Written(record);
        DataBroadcastRecord read = DataBroadcastRecordFormat.Read(written)!;

        Assert.Equal((DataBroadcastRecordFormat.FormatVersion, marks), (written[9], written[19]));
        Assert.Equal((incomplete, autoStart), (read.Incomplete, read.AutoStart));
    }

    [Fact(DisplayName = "BR-BD-005: a record cut short anywhere is no record, and reading it throws nothing")]
    public void ARecordCutShortAnywhereIsNoRecord()
    {
        for (int length = 0; length < VersionOne.Length; length++)
        {
            Assert.Null(DataBroadcastRecordFormat.Read(VersionOne.AsMemory(0, length)));
        }

        for (int length = 0; length < VersionTwo.Length; length++)
        {
            Assert.Null(DataBroadcastRecordFormat.Read(VersionTwo.AsMemory(0, length)));
        }

        Assert.Null(DataBroadcastRecordFormat.Read((byte[])[.. VersionOne, 0x00]));
        Assert.Null(DataBroadcastRecordFormat.Read((byte[])[.. VersionTwo, 0x00]));
    }

    [Theory(DisplayName = "BR-BD-005: bytes this format did not write are no record, and reading them throws nothing")]
    [InlineData(0, 0x00)]
    [InlineData(9, 0x03)]
    [InlineData(19, 0x02)]
    [InlineData(19, 0x03)]
    [InlineData(55, 0x00)]
    [InlineData(57, 0x08)]
    [InlineData(66, 0x02)]
    [InlineData(67, 0x02)]
    [InlineData(68, 0x10)]
    [InlineData(73, 0x03)]
    public void BytesThisFormatDidNotWriteAreNoRecord(int at, byte instead)
    {
        byte[] spoilt = [.. VersionOne];
        spoilt[at] = instead;

        Assert.Null(DataBroadcastRecordFormat.Read(spoilt));
    }

    [Theory(DisplayName = "BR-BD-005: a record of format version 2 marked with a bit it does not name is no record")]
    [InlineData(0x04)]
    [InlineData(0x80)]
    public void ARecordOfVersionTwoMarkedWithABitItDoesNotNameIsNoRecord(byte marks)
    {
        Assert.Null(DataBroadcastRecordFormat.Read(Rewritten(VersionTwo, (19, marks))));
        Assert.False(DataBroadcastRecordFormat.Heads(Rewritten(VersionTwo, (19, marks))));
    }

    [Fact(DisplayName = "BR-BD-005: a version last seen before it was first seen is no record")]
    public void AVersionLastSeenBeforeItWasFirstSeenIsNoRecord()
    {
        byte[] spoilt = [.. VersionOne];
        spoilt[45] = 0x00;

        Assert.Null(DataBroadcastRecordFormat.Read(spoilt));
    }

    private static DataBroadcastRecord Gathered()
        => new(
            -90_000,
            Entry,
            [
                new RecordedCarousel(Other, 9, [new ModuleVersion(Other, 0x0001, 3, 10_000, 20_000, [new CarouselResource("logo.png", "image/X-arib-png", ResourceForm.Binary, Png)])]),
                new RecordedCarousel(
                    Entry,
                    1,
                    [
                        new ModuleVersion(Entry, 0x0000, 1, -45_000, 900_000, [
                            new CarouselResource("startup.bml", "text/X-arib-bml", ResourceForm.Text, Encoding.UTF8.GetBytes("<bml>天気</bml>")),
                            new CarouselResource("style.css", "text/css", ResourceForm.Text, Encoding.UTF8.GetBytes("p{}")),
                        ]),
                        new ModuleVersion(Entry, 0x0000, 2, 900_000, 1_800_000, [
                            new CarouselResource("startup.bml", "text/X-arib-bml", ResourceForm.Text, Encoding.UTF8.GetBytes("<bml>雨</bml>")),
                        ]),
                        new ModuleVersion(Entry, 0x0003, 1, 1_000_000, 1_000_000, [
                            new CarouselResource("data.bin", "application/octet-stream", ResourceForm.Binary, new byte[] { 0x00, 0xFF }),
                        ]),
                    ]),
            ],
            [
                new EventMessage(1, 2, 1, EventTiming.Npt, 450_000, new byte[] { 0x01, 0x02 }),
                new EventMessage(1, 1, 1, EventTiming.Immediate, -9_000, ReadOnlyMemory<byte>.Empty),
            ],
            true);

    private static byte[] Rewritten(byte[] bytes, params (int At, byte Instead)[] changes)
    {
        byte[] rewritten = [.. bytes];

        foreach ((int at, byte instead) in changes)
        {
            rewritten[at] = instead;
        }

        return rewritten;
    }

    private static DataBroadcastRecord Single(CarouselResource resource)
        => new(0, Entry, [new RecordedCarousel(Entry, 1, [new ModuleVersion(Entry, 0, 1, 0, 0, [resource])])], [], false);
}
