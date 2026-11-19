using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace Carina.Infrastructure.Notifications;

public interface INoticeNetwork
{
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken);

    Task<Stream> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken);

    bool Trusts(X509Certificate? certificate, SslPolicyErrors errors);
}

public sealed class SystemNoticeNetwork : INoticeNetwork
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

    public bool Trusts(X509Certificate? certificate, SslPolicyErrors errors) => errors is SslPolicyErrors.None;
}
