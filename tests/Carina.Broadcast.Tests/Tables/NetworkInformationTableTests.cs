using Carina.Broadcast.Descriptors;
using Carina.Broadcast.Sections;
using Carina.Broadcast.Tables;
using Carina.Broadcast.Tests.Building;

namespace Carina.Broadcast.Tests.Tables;

public sealed class NetworkInformationTableTests
{
    private const int SomeNetworkId = 50001;
    private const int SomeTransportStreamId = 50002;
    private const int AnotherTransportStreamId = 50003;
    private const int SomeServiceId = 50101;
    private const int AnotherServiceId = 50102;
    private const int OneSegServiceId = 50108;

    [Fact]
    public void AScanLearnsWhichStreamsExistAndWhatEachCarries()
    {
        var table = Parse(new NitWriter
        {
            NetworkDescriptors = SiDescriptorWriter.NetworkName(new AribTextWriter().Kanji("試験").ToArray()),
            TransportStreams =
            [
                NitWriter.TransportStream(
                    SomeTransportStreamId,
                    SomeNetworkId,
                    DescriptorWriter.Loop(
                        SiDescriptorWriter.ServiceList(
                            (SomeServiceId, (int)ServiceKind.Television),
                            (OneSegServiceId, (int)ServiceKind.Television)),
                        SiDescriptorWriter.TransportStreamInformation(
                            9,
                            new AribTextWriter().Kanji("試験").ToArray(),
                            (0xFF, [SomeServiceId])),
                        SiDescriptorWriter.PartialReception(OneSegServiceId))),
                NitWriter.TransportStream(
                    AnotherTransportStreamId,
                    SomeNetworkId,
                    SiDescriptorWriter.ServiceList((AnotherServiceId, (int)ServiceKind.Audio))),
            ],
        });

        Assert.Equal(SomeNetworkId, table.NetworkId);
        Assert.True(table.IsActualNetwork);
        Assert.Equal("試験", table.NetworkName);
        Assert.Equal<int>(
            [SomeTransportStreamId, AnotherTransportStreamId],
            table.TransportStreams.Select(stream => stream.TransportStreamId).ToArray());

        var first = table.TransportStreams[0];
        Assert.Equal(SomeNetworkId, first.OriginalNetworkId);
        Assert.Equal(9, first.RemoteControlKeyId);
        Assert.Equal("試験", first.Name);
        Assert.Equal<int>(
            [SomeServiceId, OneSegServiceId],
            first.Services.Select(service => service.ServiceId).ToArray());
        Assert.Equal(ServiceKind.Television, first.Services[0].Kind);
        Assert.Equal<int>([OneSegServiceId], first.PartiallyReceivedServices.ToArray());

        Assert.Null(table.TransportStreams[1].RemoteControlKeyId);
        Assert.Empty(table.TransportStreams[1].PartiallyReceivedServices);
    }

    [Fact]
    public void TheTableForAnotherNetworkSaysSoRatherThanPassingForThisOne()
    {
        var table = Parse(new NitWriter(), tableId: NetworkInformationTable.OtherNetworkTableId);

        Assert.False(table.IsActualNetwork);
    }

    [Fact]
    public void ANetworkThatAnnouncesNoStreamsIsStillATable()
    {
        var table = Parse(new NitWriter());

        Assert.Empty(table.TransportStreams);
        Assert.Equal(string.Empty, table.NetworkName);
    }

    [Fact]
    public void ATagNoOneKnowsIsCarriedOnTheStreamRatherThanRefused()
    {
        var table = Parse(new NitWriter
        {
            TransportStreams =
            [
                NitWriter.TransportStream(
                    SomeTransportStreamId,
                    SomeNetworkId,
                    DescriptorWriter.Loop(
                        DescriptorWriter.Of(0xFA, 0x01, 0x02, 0x03),
                        SiDescriptorWriter.ServiceList((SomeServiceId, (int)ServiceKind.Television)))),
            ],
        });

        var stream = Assert.Single(table.TransportStreams);
        Assert.Equal(2, stream.Descriptors.Count);
        Assert.Equal<byte[]>([0x01, 0x02, 0x03], stream.Descriptors.WithTag(0xFA)!.Payload.ToArray());
        Assert.Single(stream.Services);
    }

    [Fact]
    public void AVersionChangeIsCarriedThroughSoTheCallerCanTellTheTablesApart()
    {
        Assert.Equal(4, Parse(new NitWriter(), version: 4).VersionNumber);
        Assert.Equal(5, Parse(new NitWriter(), version: 5).VersionNumber);
    }

    [Theory]
    [InlineData(0x42)]
    [InlineData(0x4E)]
    public void ASectionThatIsNotTheNetworkTableIsRefused(int tableId)
    {
        var rejected = Read(new NitWriter(), tableId: tableId);

        Assert.Equal(TableDefect.WrongTableId, Rejection(rejected));
    }

    [Fact]
    public void ASectionTooShortForTheFixedFieldsIsRefused()
    {
        var section = CarriedSection.Of(new SectionWriter
        {
            TableId = NetworkInformationTable.ActualNetworkTableId,
            TableIdExtension = SomeNetworkId,
            Body = [0xF0],
        });

        Assert.Equal(TableDefect.SectionTooShort, Rejection(NetworkInformationTable.Read(section)));
    }

    [Fact]
    public void ANetworkDescriptorLoopReachingPastTheSectionRefusesTheWholeTable()
    {
        var rejected = Read(new NitWriter
        {
            NetworkDescriptors = SiDescriptorWriter.NetworkName([0x41]),
            DeclaredNetworkDescriptorsLength = 200,
        });

        Assert.Equal(TableDefect.LoopOverrun, Rejection(rejected));
    }

    [Fact]
    public void AStreamLoopReachingPastTheSectionRefusesTheWholeTable()
    {
        var rejected = Read(new NitWriter
        {
            TransportStreams =
            [
                NitWriter.TransportStream(SomeTransportStreamId, SomeNetworkId, []),
            ],
            DeclaredTransportStreamLoopLength = 200,
        });

        Assert.Equal(TableDefect.LoopOverrun, Rejection(rejected));
    }

    [Fact]
    public void AStreamLoopEndingPartWayThroughAnEntryRefusesTheWholeTable()
    {
        var rejected = Read(new NitWriter
        {
            TransportStreams = [[0x01, 0x02, 0x03]],
        });

        Assert.Equal(TableDefect.LoopOverrun, Rejection(rejected));
    }

    [Fact]
    public void ADescriptorWhoseLengthOverrunsItsStreamRefusesTheWholeTable()
    {
        var rejected = Read(new NitWriter
        {
            TransportStreams =
            [
                NitWriter.TransportStream(
                    SomeTransportStreamId,
                    SomeNetworkId,
                    DescriptorWriter.Overrunning(DescriptorTags.ServiceList, declaredLength: 90, 0x01, 0x02)),
            ],
        });

        Assert.Equal(TableDefect.MalformedDescriptor, Rejection(rejected));
    }

    [Fact]
    public void ADescriptorLoopLongerThanTheStreamItBelongsToRefusesTheWholeTable()
    {
        var rejected = Read(new NitWriter
        {
            TransportStreams =
            [
                NitWriter.TransportStream(
                    SomeTransportStreamId,
                    SomeNetworkId,
                    SiDescriptorWriter.ServiceList((SomeServiceId, 0x01)),
                    declaredDescriptorsLength: 90),
            ],
        });

        Assert.Equal(TableDefect.LoopOverrun, Rejection(rejected));
    }

    private static NetworkInformationTable Parse(
        NitWriter writer,
        int tableId = NetworkInformationTable.ActualNetworkTableId,
        int version = 1)
        => Assert.IsType<TableRead<NetworkInformationTable>.Parsed>(Read(writer, tableId, version)).Table;

    private static TableDefect Rejection(TableRead<NetworkInformationTable> read)
        => Assert.IsType<TableRead<NetworkInformationTable>.Rejected>(read).Defect;

    private static TableRead<NetworkInformationTable> Read(
        NitWriter writer,
        int tableId = NetworkInformationTable.ActualNetworkTableId,
        int version = 1)
        => NetworkInformationTable.Read(CarriedSection.Of(new SectionWriter
        {
            TableId = tableId,
            TableIdExtension = SomeNetworkId,
            VersionNumber = version,
            Body = writer.ToBody(),
        }));
}
