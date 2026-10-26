namespace Carina.Domain.Streaming;

/// <summary>
/// One live session's pictures and sound cut into segments for one player.
/// </summary>
public interface ILiveHlsRendition
{
    LiveProfile Profile { get; }

    LiveHlsWindow Window { get; }

    ReadOnlyMemory<byte>? Initialisation { get; }

    ReadOnlyMemory<byte>? Segment(long number);

    /// <summary>
    /// Waits for the first segment, and says whether there is one.
    /// </summary>
    Task<bool> ReadyAsync(CancellationToken cancellationToken);
}
