using Carina.Broadcast.Descriptors;
using Carina.Broadcast.Sections;
using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.Tables;

public sealed class ServiceDescriptionTableTests
{
    private const int SomeNetworkId = 50001;
    private const int SomeTransportStreamId = 50002;
    private const int SomeServiceId = 50101;
    private const int AnotherServiceId = 50102;

    [Fact]
    public void TheServicesOfTheCurrentStreamComeBackNamedAndTold()
    {
        ServiceDescriptionTable table = Parse(new SdtWriter
        {
            OriginalNetworkId = SomeNetworkId,
            Services =
            [
                SdtWriter.Service(
                    SomeServiceId,
                    SiDescriptorWriter.Service(
                        (int)ServiceKind.Television,
                        new AribTextWriter().Kanji("試験").ToArray(),
                        new AribTextWriter().Kanji("試験").KatakanaBySingleShift("テレビ").Hiragana("その").ToArray())),
                SdtWriter.Service(
                    AnotherServiceId,
                    SiDescriptorWriter.Service((int)ServiceKind.Audio, [], []),
                    carriesScheduleEvents: false,
                    isConditionalAccess: true),
            ],
        });

        Assert.Equal(SomeTransportStreamId, table.TransportStreamId);
        Assert.Equal(SomeNetworkId, table.OriginalNetworkId);
        Assert.True(table.IsActualStream);

        DescribedService first = table.Services[0];
        Assert.Equal(SomeServiceId, first.ServiceId);
        Assert.Equal("試験テレビその", first.Name);
        Assert.Equal("試験", first.ProviderName);
        Assert.Equal(ServiceKind.Television, first.Kind);
        Assert.True(first.CarriesScheduleEvents);
        Assert.True(first.CarriesPresentFollowingEvents);
        Assert.False(first.IsConditionalAccess);

        DescribedService second = table.Services[1];
        Assert.Equal(AnotherServiceId, second.ServiceId);
        Assert.Equal(ServiceKind.Audio, second.Kind);
        Assert.False(second.CarriesScheduleEvents);
        Assert.True(second.IsConditionalAccess);
    }

    [Fact]
    public void AServiceWithNoDescriptionHasNoNameRatherThanAWrongOne()
    {
        ServiceDescriptionTable table = Parse(new SdtWriter
        {
            OriginalNetworkId = SomeNetworkId,
            Services = [SdtWriter.Service(SomeServiceId, [])],
        });

        DescribedService service = Assert.Single(table.Services);
        Assert.Equal(string.Empty, service.Name);
        Assert.Null(service.Description);
        Assert.Equal(ServiceKind.Unknown, service.Kind);
    }

    [Fact]
    public void AKindTheStandardDoesNotNameIsUnknownRatherThanGuessed()
    {
        ServiceDescriptionTable table = Parse(new SdtWriter
        {
            OriginalNetworkId = SomeNetworkId,
            Services = [SdtWriter.Service(SomeServiceId, SiDescriptorWriter.Service(0x5A, [], []))],
        });

        Assert.Equal(ServiceKind.Unknown, Assert.Single(table.Services).Kind);
        Assert.Equal(0x5A, Assert.Single(table.Services).Description!.ServiceType);
    }

    [Fact]
    public void ATemporaryServiceIsToldApartFromAPermanentOne()
    {
        ServiceDescriptionTable table = Parse(new SdtWriter
        {
            OriginalNetworkId = SomeNetworkId,
            Services =
            [
                SdtWriter.Service(
                    SomeServiceId,
                    SiDescriptorWriter.Service((int)ServiceKind.TemporaryVideo, [], [])),
            ],
        });

        Assert.Equal(ServiceKind.TemporaryVideo, Assert.Single(table.Services).Kind);
    }

    [Fact]
    public void TheTableForAnotherStreamSaysSoRatherThanPassingForThisOne()
    {
        ServiceDescriptionTable table = Parse(
            new SdtWriter { OriginalNetworkId = SomeNetworkId },
            tableId: ServiceDescriptionTable.OtherStreamTableId);

        Assert.False(table.IsActualStream);
        Assert.Empty(table.Services);
    }

    [Theory]
    [InlineData(0x40)]
    [InlineData(0x4E)]
    public void ASectionThatIsNotTheServiceTableIsRefused(int tableId)
    {
        Assert.Equal(
            TableDefect.WrongTableId,
            Rejection(Read(new SdtWriter { OriginalNetworkId = SomeNetworkId }, tableId: tableId)));
    }

    [Fact]
    public void ASectionTooShortForTheFixedFieldsIsRefused()
    {
        Section section = CarriedSection.Of(new SectionWriter
        {
            TableId = ServiceDescriptionTable.ActualStreamTableId,
            TableIdExtension = SomeTransportStreamId,
            Body = [0x00, 0x01],
        });

        Assert.Equal(TableDefect.SectionTooShort, Rejection(ServiceDescriptionTable.Read(section)));
    }

    [Fact]
    public void AServiceEntryCutShortRefusesTheWholeTable()
    {
        TableRead<ServiceDescriptionTable> rejected = Read(new SdtWriter
        {
            OriginalNetworkId = SomeNetworkId,
            Services = [[0x01, 0x02, 0x03]],
        });

        Assert.Equal(TableDefect.LoopOverrun, Rejection(rejected));
    }

    [Fact]
    public void ADescriptorLoopLongerThanTheSectionRefusesTheWholeTable()
    {
        TableRead<ServiceDescriptionTable> rejected = Read(new SdtWriter
        {
            OriginalNetworkId = SomeNetworkId,
            Services = [SdtWriter.Service(SomeServiceId, [], declaredDescriptorsLength: 90)],
        });

        Assert.Equal(TableDefect.LoopOverrun, Rejection(rejected));
    }

    [Fact]
    public void ADescriptorWhoseLengthOverrunsItsServiceRefusesTheWholeTable()
    {
        TableRead<ServiceDescriptionTable> rejected = Read(new SdtWriter
        {
            OriginalNetworkId = SomeNetworkId,
            Services =
            [
                SdtWriter.Service(
                    SomeServiceId,
                    DescriptorWriter.Overrunning(DescriptorTags.Service, declaredLength: 90, 0x01)),
            ],
        });

        Assert.Equal(TableDefect.MalformedDescriptor, Rejection(rejected));
    }

    [Fact]
    public void ATagNoOneKnowsIsCarriedOnTheServiceRatherThanRefused()
    {
        ServiceDescriptionTable table = Parse(new SdtWriter
        {
            OriginalNetworkId = SomeNetworkId,
            Services =
            [
                SdtWriter.Service(
                    SomeServiceId,
                    DescriptorWriter.Loop(
                        DescriptorWriter.Of(0xCF, 0x09),
                        SiDescriptorWriter.Service((int)ServiceKind.Television, [], []))),
            ],
        });

        DescribedService service = Assert.Single(table.Services);
        Assert.Equal(2, service.Descriptors.Count);
        Assert.Equal<byte[]>([0x09], service.Descriptors.WithTag(0xCF)!.Payload.ToArray());
        Assert.Equal(ServiceKind.Television, service.Kind);
    }

    [Fact]
    public void AVersionChangeIsCarriedThroughSoTheCallerCanTellTheTablesApart()
    {
        var writer = new SdtWriter { OriginalNetworkId = SomeNetworkId };

        Assert.Equal(2, Parse(writer, version: 2).VersionNumber);
        Assert.Equal(3, Parse(writer, version: 3).VersionNumber);
    }

    private static ServiceDescriptionTable Parse(
        SdtWriter writer,
        int tableId = ServiceDescriptionTable.ActualStreamTableId,
        int version = 1)
        => Assert.IsType<TableRead<ServiceDescriptionTable>.Parsed>(Read(writer, tableId, version)).Table;

    private static TableDefect Rejection(TableRead<ServiceDescriptionTable> read)
        => Assert.IsType<TableRead<ServiceDescriptionTable>.Rejected>(read).Defect;

    private static TableRead<ServiceDescriptionTable> Read(
        SdtWriter writer,
        int tableId = ServiceDescriptionTable.ActualStreamTableId,
        int version = 1)
        => ServiceDescriptionTable.Read(CarriedSection.Of(new SectionWriter
        {
            TableId = tableId,
            TableIdExtension = SomeTransportStreamId,
            VersionNumber = version,
            Body = writer.ToBody(),
        }));
}
