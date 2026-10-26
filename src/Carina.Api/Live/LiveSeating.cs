using Carina.Domain.Streaming;

namespace Carina.Api.Live;

public static class LiveSeating
{
    /// <summary>
    /// Seats a viewer on the session it asks for, with the sound taken from where the broadcast on air carries it.
    /// The channel is reached for while that is looked up, so that the tuner is opened meanwhile.
    /// </summary>
    public static async Task<(LiveSessionKey Key, LiveJoin Join)> JoinAsync(
        ILiveSessionManager sessions,
        ISoundOnAir onAir,
        LiveSessionKey asked,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(onAir);
        ArgumentNullException.ThrowIfNull(asked);

        using (sessions.Reach(new LiveChannelKey(asked.Network, asked.Service)))
        {
            AnnouncedSound announced = await onAir.AnnouncedAsync(asked.Network, asked.Service, cancellationToken);
            LiveSessionKey key = asked.Taking(SoundArrangement.Of(announced).Reaching(asked.Sound));

            return (key, await sessions.JoinAsync(key, cancellationToken));
        }
    }
}
