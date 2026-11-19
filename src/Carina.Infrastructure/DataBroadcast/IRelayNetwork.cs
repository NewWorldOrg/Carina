using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Carina.Infrastructure.DataBroadcast;

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
