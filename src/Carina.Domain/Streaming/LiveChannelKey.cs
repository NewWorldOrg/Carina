using Carina.Domain.Channels;

namespace Carina.Domain.Streaming;

public sealed record LiveChannelKey
{
    public LiveChannelKey(NetworkId network, ServiceId service)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(service);

        Network = network;
        Service = service;
    }

    public NetworkId Network { get; }

    public ServiceId Service { get; }

    public override string ToString() => $"{Network.Value}:{Service.Value}";
}
