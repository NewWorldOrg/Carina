namespace Carina.Domain.Streaming;

/// <summary>
/// A live session's or a recording's pictures and sound cut into segments for one player.
/// </summary>
public interface IHlsRendition
{
    LiveProfile Profile { get; }

    HlsWindow Window { get; }

    ReadOnlyMemory<byte>? Initialisation { get; }

    ReadOnlyMemory<byte>? Segment(long number);

    /// <summary>
    /// Waits for the first segment, and says whether there is one.
    /// </summary>
    Task<bool> ReadyAsync(CancellationToken cancellationToken);
}
