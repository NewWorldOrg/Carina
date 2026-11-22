using Carina.Domain.Auth;
using Carina.Domain.Streaming;

namespace Carina.Api.Live;

public static class LiveSeating
{
    public static async Task<(LiveSessionKey Key, LiveJoin Join)> JoinAsync(
        ILiveSessionManager sessions,
        ISoundOnAir onAir,
        LiveSessionKey asked,
        Subject viewer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(onAir);
        ArgumentNullException.ThrowIfNull(asked);
        ArgumentNullException.ThrowIfNull(viewer);

        using (sessions.Reach(new LiveChannelKey(asked.Network, asked.Service)))
        {
            LiveSessionKey key = await KeyAsync(onAir, asked, cancellationToken);

            return (key, await sessions.JoinAsync(key, viewer, cancellationToken));
        }
    }

    public static async Task<(LiveSessionKey Key, LiveAhead Ahead)> StartAheadAsync(
        ILiveSessionManager sessions,
        ISoundOnAir onAir,
        LiveSessionKey asked,
        Subject viewer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(onAir);
        ArgumentNullException.ThrowIfNull(asked);
        ArgumentNullException.ThrowIfNull(viewer);

        using (sessions.Reach(new LiveChannelKey(asked.Network, asked.Service)))
        {
            LiveSessionKey key = await KeyAsync(onAir, asked, cancellationToken);

            return (key, sessions.StartAhead(key, viewer));
        }
    }

    private static async Task<LiveSessionKey> KeyAsync(
        ISoundOnAir onAir,
        LiveSessionKey asked,
        CancellationToken cancellationToken)
    {
        AnnouncedSound announced = await onAir.AnnouncedAsync(asked.Network, asked.Service, cancellationToken);

        return asked.Taking(SoundArrangement.Of(announced).Reaching(asked.Sound));
    }
}
