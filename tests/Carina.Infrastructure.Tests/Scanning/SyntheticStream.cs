using Carina.Broadcast.Descriptors;
using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;

namespace Carina.Infrastructure.Tests.Scanning;

public sealed record SyntheticService(
    int ServiceId,
    string Name,
    ServiceKind Kind = ServiceKind.Television,
    bool PartiallyReceived = false);

public sealed class SyntheticStream
{
    public const int SomeNetworkId = 50001;

    public required int NetworkId { get; init; }

    public required int TransportStreamId { get; init; }

    public IReadOnlyList<SyntheticService> Services { get; init; } = [];

    public int? TransportStreamIdInNetwork { get; init; }

    public IReadOnlyList<int> OtherStreamsInNetwork { get; init; } = [];

    public bool WithoutNetwork { get; init; }

    public bool WithoutDescription { get; init; }

    public static SyntheticStream Carrying(int transportStreamId, params SyntheticService[] services)
        => new()
        {
            NetworkId = SomeNetworkId,
            TransportStreamId = transportStreamId,
            Services = services,
        };

    public byte[] ToBytes()
    {
        var packets = new List<byte[]>();

        if (!WithoutNetwork)
        {
            packets.AddRange(new TransportStreamWriter(NetworkInformationTable.Pid)
                .Sections(NetworkSection())
                .Packets);
        }

        if (!WithoutDescription)
        {
            packets.AddRange(new TransportStreamWriter(ServiceDescriptionTable.Pid)
                .Sections(DescriptionSection())
                .Packets);
        }

        return [.. packets.SelectMany(packet => packet)];
    }

    private byte[] NetworkSection()
    {
        var listed = TransportStreamIdInNetwork ?? TransportStreamId;
        var partiallyReceived = Services
            .Where(service => service.PartiallyReceived)
            .Select(service => service.ServiceId)
            .ToArray();

        var descriptors = new List<byte[]>
        {
            SiDescriptorWriter.ServiceList(
                [.. Services.Select(service => (service.ServiceId, (int)service.Kind))]),
        };

        if (partiallyReceived.Length > 0)
        {
            descriptors.Add(SiDescriptorWriter.PartialReception(partiallyReceived));
        }

        var streams = new List<byte[]>
        {
            NitWriter.TransportStream(listed, NetworkId, DescriptorWriter.Loop([.. descriptors])),
        };

        streams.AddRange(OtherStreamsInNetwork.Select(other =>
            NitWriter.TransportStream(other, NetworkId, SiDescriptorWriter.ServiceList())));

        return new SectionWriter
        {
            TableId = NetworkInformationTable.ActualNetworkTableId,
            TableIdExtension = NetworkId,
            Body = new NitWriter
            {
                NetworkDescriptors = SiDescriptorWriter.NetworkName(
                    new AribTextWriter().Ascii("Carina").ToArray()),
                TransportStreams = [.. streams],
            }.ToBody(),
        }.ToBytes();
    }

    private byte[] DescriptionSection()
        => new SectionWriter
        {
            TableId = ServiceDescriptionTable.ActualStreamTableId,
            TableIdExtension = TransportStreamId,
            Body = new SdtWriter
            {
                OriginalNetworkId = NetworkId,
                Services =
                [
                    .. Services.Select(service => SdtWriter.Service(
                        service.ServiceId,
                        SiDescriptorWriter.Service(
                            (int)service.Kind,
                            new AribTextWriter().Ascii("Carina").ToArray(),
                            new AribTextWriter().Ascii(service.Name).ToArray()))),
                ],
            }.ToBody(),
        }.ToBytes();
}
