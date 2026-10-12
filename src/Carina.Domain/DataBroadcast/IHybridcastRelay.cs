using System.Net.WebSockets;

namespace Carina.Domain.DataBroadcast;

/// <summary>
/// What an AIT asked of a broadcaster's server came back as: the result, the HTTP status where the server answered,
/// and the applications it names where it answered with an AIT that could be read.
/// </summary>
public sealed record RelayedAit(RelayResult Result, int? HttpStatus, IReadOnlyList<HybridcastApplication>? Applications)
{
    public static RelayedAit Failed(RelayResult result) => new(result, null, null);
}

/// <summary>
/// What a document or resource of a Hybridcast application came back as: the result, and where the server
/// answered, its status, the media type it named, the body and the address the body was finally taken from.
/// </summary>
public sealed record RelayedBody(RelayResult Result, int? HttpStatus, string? MediaType, ReadOnlyMemory<byte> Body, Uri? At)
{
    public static RelayedBody Failed(RelayResult result) => new(result, null, null, ReadOnlyMemory<byte>.Empty, null);
}

/// <summary>
/// A WebSocket opened to a broadcaster's server for a Hybridcast application, waiting for the application's own
/// to be accepted on the subprotocol the server chose and carried both ways until either side closes.
/// </summary>
public interface IHybridcastSocket : IAsyncDisposable
{
    string? SubProtocol { get; }

    Task CarryAsync(WebSocket viewer, CancellationToken cancellationToken);
}

/// <summary>
/// What opening a WebSocket to a broadcaster's server came to: the result, the HTTP status where the server answered
/// with something other than the upgrade, and the socket where it upgraded.
/// </summary>
public sealed record RelayedSocket(RelayResult Result, int? HttpStatus, IHybridcastSocket? Socket)
{
    public static RelayedSocket Failed(RelayResult result) => new(result, null, null);
}

/// <summary>
/// Talks to a broadcaster's server for a Hybridcast application started from a live channel: it reads the AIT a
/// document names, and carries the application's documents, resources and WebSockets, never beyond its boundary.
/// </summary>
public interface IHybridcastRelay
{
    Task<RelayedAit> ReadAitAsync(string channel, string uri, CancellationToken cancellationToken);

    Task<RelayedBody> FetchAsync(string channel, Uri address, HybridcastBoundary boundary, CancellationToken cancellationToken);

    Task<RelayedSocket> OpenSocketAsync(
        string channel,
        Uri address,
        HybridcastBoundary boundary,
        IReadOnlyList<string> subProtocols,
        CancellationToken cancellationToken);
}
