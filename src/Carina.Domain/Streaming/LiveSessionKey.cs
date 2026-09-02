using Carina.Domain.Channels;

namespace Carina.Domain.Streaming;

public sealed record LiveSessionKey
{
    public LiveSessionKey(NetworkId network, ServiceId service, LiveProfile profile)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(profile);

        Network = network;
        Service = service;
        Profile = profile;
    }

    public NetworkId Network { get; }

    public ServiceId Service { get; }

    public LiveProfile Profile { get; }

    public override string ToString() => $"{Network.Value}:{Service.Value}:{Profile.Name}";
}
