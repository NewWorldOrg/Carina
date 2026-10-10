using Carina.Broadcast.DsmCc;
using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.DsmCc;

public sealed class DataBroadcastStreamTests
{
    private const int EntryPid = 0x0140;

    private const int OtherCarouselPid = 0x0150;

    private const int OtherCarouselTag = 0x50;

    private const int OneSegDataComponentId = 0x000D;

    private const int Resolution960By540 = 0x02;

    [Fact]
    public void BR_BD_001_TheStreamCarryingTheEntryCarouselIsFoundWithItsComponentTag()
    {
        DataBroadcastService service = Find(
            BxmlInfoWriter.DataBroadcastStream(
                EntryPid,
                BxmlInfoWriter.EntryComponentTag,
                BxmlInfoWriter.Entry(autoStart: false, Resolution960By540, 1, 0, dataEventId: 3)),
            BxmlInfoWriter.DataBroadcastStream(OtherCarouselPid, OtherCarouselTag, BxmlInfoWriter.NotEntry(dataEventId: 3)));

        Assert.True(service.IsCarried);
        Assert.Equal([EntryPid, OtherCarouselPid], service.Streams.Select(stream => stream.Pid));
        Assert.Equal([DataBroadcastStreams.EntryComponentTag, OtherCarouselTag], service.Streams.Select(stream => stream.ComponentTag));
        Assert.Equal(EntryPid, service.Entry!.Pid);
        Assert.True(service.Entry.IsEntry);
        Assert.False(service.Streams[1].IsEntry);
    }

    [Fact]
    public void BR_BD_001_TheEntryCarriesTheAutoStartFlagAndWhatTheStartDocumentAsksFor()
    {
        DataBroadcastService service = Find(BxmlInfoWriter.DataBroadcastStream(
            EntryPid,
            BxmlInfoWriter.EntryComponentTag,
            BxmlInfoWriter.Entry(autoStart: true, Resolution960By540, 0x0102, 0x0304, dataEventId: 5)));

        BxmlInfo info = service.Entry!.Bxml!;
        Assert.True(info.IsEntryPoint);
        Assert.True(info.AutoStart);
        Assert.Equal(Resolution960By540, info.DocumentResolution);
        Assert.Equal(0x0102, info.BmlMajorVersion);
        Assert.Equal(0x0304, info.BmlMinorVersion);
        Assert.Equal(5, info.DataEventId);
    }

    [Fact]
    public void BR_BD_001_AStreamThatIsNotAnEntryPointReadsItsCarouselInformationRightAfterTheFirstByte()
    {
        DataBroadcastService service = Find(
            BxmlInfoWriter.DataBroadcastStream(EntryPid, BxmlInfoWriter.EntryComponentTag, BxmlInfoWriter.Entry(false, 0, 1, 0, 1)),
            BxmlInfoWriter.DataBroadcastStream(OtherCarouselPid, OtherCarouselTag, BxmlInfoWriter.NotEntry(dataEventId: 9)));

        BxmlInfo info = service.Streams[1].Bxml!;
        Assert.False(info.IsEntryPoint);
        Assert.False(info.AutoStart);
        Assert.Equal(9, info.DataEventId);
    }

    [Fact]
    public void BR_BD_001_AStreamOfAnotherDataComponentLikeOneSegIsNotADataBroadcast()
    {
        DataBroadcastService service = Find(PmtWriter.Stream(
            PmtWriter.DsmCcSections,
            EntryPid,
            DescriptorWriter.Loop(
                PsiDescriptorWriter.StreamIdentifier(BxmlInfoWriter.EntryComponentTag),
                PsiDescriptorWriter.DataComponent(OneSegDataComponentId, BxmlInfoWriter.Entry(false, 0, 1, 0, 1)))));

        Assert.Empty(service.Streams);
        Assert.False(service.IsCarried);
        Assert.Null(service.Entry);
    }

    [Fact]
    public void BR_BD_001_AStreamOfAnotherStreamTypeIsNotADataBroadcastEvenWithTheDataComponent()
    {
        DataBroadcastService service = Find(PmtWriter.Stream(
            PmtWriter.PrivateData,
            EntryPid,
            DescriptorWriter.Loop(
                PsiDescriptorWriter.StreamIdentifier(BxmlInfoWriter.EntryComponentTag),
                PsiDescriptorWriter.DataComponent(BxmlInfoWriter.BxmlDataComponentId, BxmlInfoWriter.Entry(false, 0, 1, 0, 1)))));

        Assert.False(service.IsCarried);
    }

    [Fact]
    public void BR_BD_001_AServiceWithCarouselsButNoEntryTagCarriesNoDataBroadcast()
    {
        DataBroadcastService service = Find(
            BxmlInfoWriter.DataBroadcastStream(OtherCarouselPid, OtherCarouselTag, BxmlInfoWriter.NotEntry(dataEventId: 1)));

        Assert.Single(service.Streams);
        Assert.False(service.IsCarried);
        Assert.Null(service.Entry);
    }

    [Fact]
    public void BR_BD_001_AStreamWithoutAStreamIdentifierHasNoTagToBeAddressedByAndIsLeftOut()
    {
        DataBroadcastService service = Find(PmtWriter.Stream(
            PmtWriter.DsmCcSections,
            EntryPid,
            PsiDescriptorWriter.DataComponent(BxmlInfoWriter.BxmlDataComponentId, BxmlInfoWriter.Entry(false, 0, 1, 0, 1))));

        Assert.Empty(service.Streams);
    }

    [Fact]
    public void BR_BD_001_ABxmlInfoCutShortLeavesTheStreamFoundButItsInformationUnread()
    {
        DataBroadcastService service = Find(BxmlInfoWriter.DataBroadcastStream(EntryPid, BxmlInfoWriter.EntryComponentTag, [0x20, 0x0F, 0x00]));

        Assert.True(service.IsCarried);
        Assert.Null(service.Entry!.Bxml);
    }

    [Fact]
    public void BR_BD_001_AStreamWithoutAnyBxmlInfoIsStillFound()
    {
        DataBroadcastService service = Find(BxmlInfoWriter.DataBroadcastStream(EntryPid, BxmlInfoWriter.EntryComponentTag, []));

        Assert.True(service.IsCarried);
        Assert.Null(service.Entry!.Bxml);
    }

    private static DataBroadcastService Find(params byte[][] streams)
    {
        ProgramMapTable table = Assert.IsType<TableRead<ProgramMapTable>.Parsed>(ProgramMapTable.Read(CarriedSection.Of(new PmtWriter
        {
            ProgramNumber = 0x0400,
            Streams = [PmtWriter.Stream(0x02, 0x0111, []), .. streams],
        }.ToSection()))).Table;

        return DataBroadcastStreams.Find(table);
    }
}
