using System.Net;
using System.Net.Sockets;

namespace Carina.Infrastructure.DataBroadcast;

/// <summary>
/// The two things the relay asks of the network: what a host name resolves to, and a connection to one address.
/// Which addresses may be connected to is decided by the relay, not here.
/// </summary>
public interface IRelayNetwork
{
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken);

    Task<Stream> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken);
}

public sealed class SystemRelayNetwork : IRelayNetwork
{
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
        => await Dns.GetHostAddressesAsync(host, cancellationToken);

    public async Task<Stream> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        Socket socket = new(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

        try
        {
            await socket.ConnectAsync(endpoint, cancellationToken);

            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();

            throw;
        }
    }
}
