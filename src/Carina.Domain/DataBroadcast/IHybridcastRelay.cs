using System.Net.WebSockets;

namespace Carina.Domain.DataBroadcast;

public sealed record RelayedAit(RelayResult Result, int? HttpStatus, IReadOnlyList<HybridcastApplication>? Applications)
{
    public static RelayedAit Failed(RelayResult result) => new(result, null, null);
}

public sealed record RelayedBody(RelayResult Result, int? HttpStatus, string? ContentType, ReadOnlyMemory<byte> Body, Uri? At)
{
    public static RelayedBody Failed(RelayResult result) => new(result, null, null, ReadOnlyMemory<byte>.Empty, null);
}

public sealed record PostedBody(ReadOnlyMemory<byte> Bytes, string? ContentType);

public interface IHybridcastSocket : IAsyncDisposable
{
    string? SubProtocol { get; }

    Task CarryAsync(WebSocket viewer, CancellationToken cancellationToken);
}

public sealed record RelayedSocket(RelayResult Result, int? HttpStatus, IHybridcastSocket? Socket)
{
    public static RelayedSocket Failed(RelayResult result) => new(result, null, null);
}

public interface IHybridcastRelay
{
    Task<RelayedAit> ReadAitAsync(string channel, string uri, CancellationToken cancellationToken);

    Task<RelayedBody> FetchAsync(string channel, Uri address, HybridcastBoundary boundary, CancellationToken cancellationToken);

    Task<RelayedBody> PostAsync(string channel, Uri address, HybridcastBoundary boundary, PostedBody posted, CancellationToken cancellationToken);

    Task<RelayedSocket> OpenSocketAsync(
        string channel,
        Uri address,
        HybridcastBoundary boundary,
        IReadOnlyList<string> subProtocols,
        CancellationToken cancellationToken);
}
