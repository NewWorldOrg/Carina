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
        Placement = SoundPlacement.WholeStream(SoundTracks.Ordinal(sound));
    }

    private LiveSessionKey(LiveSessionKey taken, SoundPlacement placement)
    {
        Network = taken.Network;
        Service = taken.Service;
        Profile = taken.Profile;
        Sound = taken.Sound;
        Placement = placement;
    }

    public NetworkId Network { get; }

    public ServiceId Service { get; }

    public LiveProfile Profile { get; }

    public SoundTrack Sound { get; }

    public SoundPlacement Placement { get; }

    /// <summary>
    /// The same channel, profile and sound, with the sound taken from where the broadcast on air carries it.
    /// </summary>
    public LiveSessionKey Taking(SoundPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);

        return new LiveSessionKey(this, placement);
    }

    public override string ToString()
        => Placement.Channel is { } channel
            ? $"{Network.Value}:{Service.Value}:{Profile.Name}:{SoundTracks.NameOf(Sound)}:{Said(channel)}"
            : $"{Network.Value}:{Service.Value}:{Profile.Name}:{SoundTracks.NameOf(Sound)}";

    private static string Said(SoundChannel channel) => channel is SoundChannel.Left ? "left" : "right";
}
