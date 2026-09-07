using Carina.Domain.Channels;

namespace Carina.Domain.Migration;

public sealed record ServiceKey
{
    public ServiceKey(NetworkId network, ServiceId service)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(service);

        Network = network;
        Service = service;
    }

    public NetworkId Network { get; }

    public ServiceId Service { get; }

    public static ServiceKey Of(int network, int service) => new(new NetworkId(network), new ServiceId(service));

    public override string ToString() => $"{Network.Value}/{Service.Value}";
}
