using Carina.Domain.Auth;

namespace Carina.Domain.Streaming;

public interface ILiveSessionManager
{
    Task<LiveJoin> JoinAsync(LiveSessionKey key, Subject viewer, CancellationToken cancellationToken);

    LiveAhead StartAhead(LiveSessionKey key, Subject viewer);

    Task<LiveHandover> HandOverAsync(LiveChannelKey channel, CancellationToken cancellationToken);

    IDisposable Reach(LiveChannelKey channel);
}
