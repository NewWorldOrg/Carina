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

    [Fact(DisplayName = "BR-BD-001: the stream carrying the entry carousel is found with its component tag")]
    public void TheStreamCarryingTheEntryCarouselIsFoundWithItsComponentTag()
    {
        DataBroadcastService service = Find(
            BxmlInfoWriter.DataBroadcastStream(
                EntryPid,
                BxmlInfoWriter.EntryComponentTag,
                BxmlInfoWriter.Entry(autoStart: false, Resolution960By540, 1, 0, dataEventId: 3)),
            BxmlInfoWriter.DataBroadcastStream(OtherCarouselPid, OtherCarouselTag, BxmlInfoWriter.NotEntry(dataEventId: 3)));

        Assert.True(service.IsCarried);
        Assert.Empty(service.Defects);
        Assert.Equal([EntryPid, OtherCarouselPid], service.Streams.Select(stream => stream.Pid));
        Assert.Equal([DataBroadcastStreams.EntryComponentTag, OtherCarouselTag], service.Streams.Select(stream => stream.ComponentTag));
        Assert.Equal(EntryPid, service.Entry!.Pid);
        Assert.True(service.Entry.HasEntryComponentTag);
        Assert.False(service.Streams[1].HasEntryComponentTag);
    }

    [Fact(DisplayName = "BR-BD-001: the entry carries the auto start flag and what the start document asks for")]
    public void TheEntryCarriesTheAutoStartFlagAndWhatTheStartDocumentAsksFor()
    {
        DataBroadcastService service = Find(BxmlInfoWriter.DataBroadcastStream(
            EntryPid,
            BxmlInfoWriter.EntryComponentTag,
            BxmlInfoWriter.Entry(autoStart: true, Resolution960By540, 0x0102, 0x0304, dataEventId: 5)));

        BxmlInfo info = service.Entry!.Bxml!;
        Assert.True(info.EntryPointFlag);
        Assert.True(info.AutoStart);
        Assert.Equal(Resolution960By540, info.DocumentResolution);
        Assert.Equal(0x0102, info.BmlMajorVersion);
        Assert.Equal(0x0304, info.BmlMinorVersion);
        Assert.Equal(5, info.DataEventId);
    }

    [Fact(DisplayName = "BR-BD-001: an entry on the default version carries no BML version and its data event id follows the flags")]
    public void AnEntryOnTheDefaultVersionCarriesNoBmlVersionAndItsDataEventIdFollowsTheFlags()
    {
        BxmlInfo info = Read(BxmlInfoWriter.Entry(autoStart: true, Resolution960By540, 0, 0, dataEventId: 6, defaultVersion: true));

        Assert.True(info.UsesDefaultVersion);
        Assert.True(info.AutoStart);
        Assert.Null(info.BmlMajorVersion);
        Assert.Null(info.BmlMinorVersion);
        Assert.Equal(6, info.DataEventId);
    }

    [Fact(DisplayName = "BR-BD-001: an entry that uses XML carries the BXML version after the BML version")]
    public void AnEntryThatUsesXmlCarriesTheBxmlVersionAfterTheBmlVersion()
    {
        BxmlInfo info = Read(BxmlInfoWriter.Entry(false, Resolution960By540, 0x0100, 0x0002, dataEventId: 4, useXml: true, bxmlMajorVersion: 0x0300, bxmlMinorVersion: 0x0004));

        Assert.True(info.UsesXml);
        Assert.Equal(0x0100, info.BmlMajorVersion);
        Assert.Equal(0x0002, info.BmlMinorVersion);
        Assert.Equal(0x0300, info.BxmlMajorVersion);
        Assert.Equal(0x0004, info.BxmlMinorVersion);
        Assert.Equal(4, info.DataEventId);
    }

    [Fact(DisplayName = "BR-BD-001: an entry on the default version that uses XML carries no version at all")]
    public void AnEntryOnTheDefaultVersionThatUsesXmlCarriesNoVersionAtAll()
    {
        BxmlInfo info = Read(BxmlInfoWriter.Entry(false, 0, 0, 0, dataEventId: 2, defaultVersion: true, useXml: true));

        Assert.Null(info.BxmlMajorVersion);
        Assert.Equal(2, info.DataEventId);
    }

    [Fact(DisplayName = "BR-BD-001: a transmission format of 01 carries a reserved byte and no carousel information")]
    public void ATransmissionFormatOf01CarriesAReservedByteAndNoCarouselInformation()
    {
        BxmlInfo info = Read(BxmlInfoWriter.Entry(false, 0, 1, 0, dataEventId: 2, transmissionFormat: 1));

        Assert.Equal(1, info.TransmissionFormat);
        Assert.Null(info.DataEventId);
        Assert.Equal(1, info.BmlMajorVersion);
    }

    [Theory(DisplayName = "BR-BD-001: an additional_arib_bxml_info cut short at any length is left unread")]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void AnAdditionalAribBxmlInfoCutShortAtAnyLengthIsLeftUnread(bool defaultVersion, bool useXml)
    {
        byte[] whole = BxmlInfoWriter.Entry(true, 2, 1, 0, 3, defaultVersion, useXml, 1, 0);

        for (int length = 1; length < whole.Length; length++)
        {
            Assert.Null(BxmlInfo.Read(whole[..length]));
        }

        Assert.NotNull(BxmlInfo.Read(whole));
    }

    [Fact(DisplayName = "BR-BD-001: a stream that is not an entry point reads its carousel information right after the first byte")]
    public void AStreamThatIsNotAnEntryPointReadsItsCarouselInformationRightAfterTheFirstByte()
    {
        DataBroadcastService service = Find(
            BxmlInfoWriter.DataBroadcastStream(EntryPid, BxmlInfoWriter.EntryComponentTag, BxmlInfoWriter.Entry(false, 0, 1, 0, 1)),
            BxmlInfoWriter.DataBroadcastStream(OtherCarouselPid, OtherCarouselTag, BxmlInfoWriter.NotEntry(dataEventId: 9)));

        BxmlInfo info = service.Streams[1].Bxml!;
        Assert.False(info.EntryPointFlag);
        Assert.False(info.AutoStart);
        Assert.Equal(9, info.DataEventId);
    }

    [Fact(DisplayName = "BR-BD-001: a stream of another data component like one-seg is not a data broadcast")]
    public void AStreamOfAnotherDataComponentLikeOneSegIsNotADataBroadcast()
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

    [Fact(DisplayName = "BR-BD-001: a stream of another stream type is not a data broadcast even with the data component")]
    public void AStreamOfAnotherStreamTypeIsNotADataBroadcastEvenWithTheDataComponent()
    {
        DataBroadcastService service = Find(PmtWriter.Stream(
            PmtWriter.PrivateData,
            EntryPid,
            DescriptorWriter.Loop(
                PsiDescriptorWriter.StreamIdentifier(BxmlInfoWriter.EntryComponentTag),
                PsiDescriptorWriter.DataComponent(BxmlInfoWriter.BxmlDataComponentId, BxmlInfoWriter.Entry(false, 0, 1, 0, 1)))));

        Assert.False(service.IsCarried);
    }

    [Fact(DisplayName = "BR-BD-001: a service with carousels but no entry tag carries no data broadcast")]
    public void AServiceWithCarouselsButNoEntryTagCarriesNoDataBroadcast()
    {
        DataBroadcastService service = Find(
            BxmlInfoWriter.DataBroadcastStream(OtherCarouselPid, OtherCarouselTag, BxmlInfoWriter.NotEntry(dataEventId: 1)));

        Assert.Single(service.Streams);
        Assert.False(service.IsCarried);
        Assert.Null(service.Entry);
    }

    [Fact(DisplayName = "BR-BD-001: a stream without a stream identifier has no tag to be addressed by and is left out")]
    public void AStreamWithoutAStreamIdentifierHasNoTagToBeAddressedByAndIsLeftOut()
    {
        DataBroadcastService service = Find(PmtWriter.Stream(
            PmtWriter.DsmCcSections,
            EntryPid,
            PsiDescriptorWriter.DataComponent(BxmlInfoWriter.BxmlDataComponentId, BxmlInfoWriter.Entry(false, 0, 1, 0, 1))));

        Assert.Empty(service.Streams);
        Assert.Equal([new DataBroadcastStreamDefect(EntryPid, DataBroadcastDefect.MissingStreamIdentifier)], service.Defects);
    }

    [Fact(DisplayName = "BR-BD-001: an additional_arib_bxml_info cut short leaves the stream found but its information unread")]
    public void ABxmlInfoCutShortLeavesTheStreamFoundButItsInformationUnread()
    {
        DataBroadcastService service = Find(BxmlInfoWriter.DataBroadcastStream(EntryPid, BxmlInfoWriter.EntryComponentTag, [0x20, 0x0F, 0x00]));

        Assert.True(service.IsCarried);
        Assert.Null(service.Entry!.Bxml);
        Assert.Equal([new DataBroadcastStreamDefect(EntryPid, DataBroadcastDefect.MalformedBxmlInfo)], service.Defects);
    }

    [Fact(DisplayName = "BR-BD-001: a stream without any additional_arib_bxml_info is still found")]
    public void AStreamWithoutAnyBxmlInfoIsStillFound()
    {
        DataBroadcastService service = Find(BxmlInfoWriter.DataBroadcastStream(EntryPid, BxmlInfoWriter.EntryComponentTag, []));

        Assert.True(service.IsCarried);
        Assert.Null(service.Entry!.Bxml);
        Assert.Empty(service.Defects);
    }

    [Fact(DisplayName = "BR-BD-001: a stream identifier without its tag and a data component cut short are reported apart from streams that carry none")]
    public void AStreamIdentifierWithoutItsTagAndADataComponentCutShortAreReportedApartFromStreamsThatCarryNone()
    {
        DataBroadcastService service = Find(
            PmtWriter.Stream(
                PmtWriter.DsmCcSections,
                EntryPid,
                DescriptorWriter.Loop(
                    DescriptorWriter.Of(PsiDescriptorWriter.StreamIdentifierTag),
                    PsiDescriptorWriter.DataComponent(BxmlInfoWriter.BxmlDataComponentId, BxmlInfoWriter.Entry(false, 0, 1, 0, 1)))),
            PmtWriter.Stream(
                PmtWriter.DsmCcSections,
                OtherCarouselPid,
                DescriptorWriter.Loop(
                    PsiDescriptorWriter.StreamIdentifier(OtherCarouselTag),
                    DescriptorWriter.Of(PsiDescriptorWriter.DataComponentTag, 0x00))),
            PmtWriter.Stream(PmtWriter.DsmCcSections, 0x0160, PsiDescriptorWriter.StreamIdentifier(0x60)));

        Assert.Empty(service.Streams);
        Assert.Equal(
            [
                new DataBroadcastStreamDefect(EntryPid, DataBroadcastDefect.MalformedStreamIdentifier),
                new DataBroadcastStreamDefect(OtherCarouselPid, DataBroadcastDefect.MalformedDataComponent),
            ],
            service.Defects);
    }

    private static BxmlInfo Read(byte[] bxmlInfo)
        => Find(BxmlInfoWriter.DataBroadcastStream(EntryPid, BxmlInfoWriter.EntryComponentTag, bxmlInfo)).Entry!.Bxml!;

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
