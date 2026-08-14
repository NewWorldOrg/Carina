using Carina.Broadcast.Tables;

namespace Carina.Infrastructure.Scanning;

public sealed record HarvestedNetwork(
    int NetworkId,
    string Name,
    IReadOnlyList<NetworkTransportStream> TransportStreams)
{
    public bool Carries(int transportStreamId)
        => TransportStreams.Any(stream => stream.TransportStreamId == transportStreamId);

    public IReadOnlyList<int> PartiallyReceivedServicesOf(int transportStreamId)
        => [.. TransportStreams
            .Where(stream => stream.TransportStreamId == transportStreamId)
            .SelectMany(stream => stream.PartiallyReceivedServices)];
}

public sealed record HarvestedDescription(
    int TransportStreamId,
    int OriginalNetworkId,
    IReadOnlyList<DescribedService> Services);
