namespace Carina.Domain.Streaming;

public interface ILiveSessionManager
{
    Task<LiveJoin> JoinAsync(LiveSessionKey key, CancellationToken cancellationToken);

    Task<LiveHandover> HandOverAsync(LiveChannelKey channel, CancellationToken cancellationToken);

    /// <summary>
    /// Starts reading the channel before a viewer asks for a session on it, so that the tuner is opened while the rest
    /// of what the viewer asks for is still being worked out. Letting go of what is handed back lets go of the reading
    /// too, unless a session has taken it up by then.
    /// </summary>
    IDisposable Reach(LiveChannelKey channel);
}
