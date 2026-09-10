using Carina.Domain.Channels;

namespace Carina.Domain.Streaming;

public sealed record LiveSessionKey
{
    public LiveSessionKey(
        NetworkId network,
        ServiceId service,
        LiveProfile profile,
        SoundTrack sound = SoundTrack.Main)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(profile);

        if (!Enum.IsDefined(sound))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sound),
                sound,
                "A picture is carried with one of the sounds named here.");
        }

        Network = network;
        Service = service;
        Profile = profile;
        Sound = sound;
    }

    public NetworkId Network { get; }

    public ServiceId Service { get; }

    public LiveProfile Profile { get; }

    public SoundTrack Sound { get; }

    public override string ToString()
        => $"{Network.Value}:{Service.Value}:{Profile.Name}:{SoundTracks.NameOf(Sound)}";
}
