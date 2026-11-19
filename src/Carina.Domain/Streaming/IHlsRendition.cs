namespace Carina.Domain.Streaming;

public interface IHlsRendition
{
    LiveProfile Profile { get; }

    HlsWindow Window { get; }

    ReadOnlyMemory<byte>? Initialisation { get; }

    ReadOnlyMemory<byte>? Segment(long number);

    Task<bool> ReadyAsync(CancellationToken cancellationToken);

    bool Broke { get; }
}
