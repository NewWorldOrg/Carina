using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Carina.Infrastructure.DataBroadcast;

/// <summary>
/// The three things the relay asks of the network: what a host name resolves to, which addresses this machine
/// answers at, which take in every address the application can be listening on, and a connection to one address.
/// Which addresses may be connected to is decided by the relay, not here.
/// </summary>
public interface IRelayNetwork
{
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken);

    IReadOnlyCollection<IPAddress> OwnAddresses();

    Task<Stream> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken);
}

public sealed class SystemRelayNetwork : IRelayNetwork
{
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
        => await Dns.GetHostAddressesAsync(host, cancellationToken);

    public IReadOnlyCollection<IPAddress> OwnAddresses()
        =>
        [
            .. NetworkInterface.GetAllNetworkInterfaces()
                .SelectMany(network => network.GetIPProperties().UnicastAddresses)
                .Select(unicast => unicast.Address),
        ];

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
