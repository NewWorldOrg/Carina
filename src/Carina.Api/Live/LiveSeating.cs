using Carina.Domain.Streaming;

namespace Carina.Api.Live;

public static class LiveSeating
{
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
