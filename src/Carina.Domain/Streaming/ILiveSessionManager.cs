namespace Carina.Domain.Streaming;

public interface ILiveSessionManager
{
    Task<LiveJoin> JoinAsync(LiveSessionKey key, CancellationToken cancellationToken);
}
