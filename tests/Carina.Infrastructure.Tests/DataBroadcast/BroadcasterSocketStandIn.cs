using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace Carina.Infrastructure.Tests.DataBroadcast;

/// <summary>
/// A broadcaster's WebSocket server on the loopback interface: it upgrades every request it is sent, choosing the
/// first subprotocol it is offered, keeps the head of each request, and hands the socket to what the test says to do
/// with it.
/// </summary>
internal sealed class BroadcasterSocketStandIn : IAsyncDisposable
{
    private const string Guid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    private readonly TcpListener listener = new(IPAddress.Loopback, 0);

    private readonly Func<WebSocket, Task> serve;

    private readonly CancellationTokenSource stopping = new();

    private readonly Task accepting;

    public BroadcasterSocketStandIn(Func<WebSocket, Task> serve)
    {
        this.serve = serve;
        listener.Start();
        accepting = AcceptAsync();
    }

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public ConcurrentQueue<IReadOnlyDictionary<string, string>> Heard { get; } = new();

    public ConcurrentQueue<Task> Served { get; } = new();

    public static async Task EchoAsync(WebSocket socket)
    {
        byte[] buffer = new byte[64 * 1024];

        while (true)
        {
            WebSocketReceiveResult received = await socket.ReceiveAsync(buffer, CancellationToken.None);

            if (received.MessageType is WebSocketMessageType.Close)
            {
                await socket.CloseOutputAsync(socket.CloseStatus ?? WebSocketCloseStatus.Empty, socket.CloseStatusDescription, CancellationToken.None);

                return;
            }

            byte[] answer = [.. "echo:"u8, .. buffer.AsSpan(0, received.Count)];
            await socket.SendAsync(answer, received.MessageType, received.EndOfMessage, CancellationToken.None);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        listener.Stop();

        try
        {
            await accepting;
        }
        catch (Exception failure) when (failure is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }

        stopping.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!stopping.IsCancellationRequested)
        {
            TcpClient connection = await listener.AcceptTcpClientAsync(stopping.Token);
            Served.Enqueue(Task.Run(() => UpgradeAsync(connection)));
        }
    }

    private async Task UpgradeAsync(TcpClient connection)
    {
        using TcpClient held = connection;
        NetworkStream stream = held.GetStream();
        Dictionary<string, string> headers = await ReadHeadAsync(stream);
        Heard.Enqueue(headers);

        string accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(headers["Sec-WebSocket-Key"] + Guid)));
        string? chosen = headers.TryGetValue("Sec-WebSocket-Protocol", out string? offered) ? offered.Split(',')[0].Trim() : null;
        string answer = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n"
                        + $"Sec-WebSocket-Accept: {accept}\r\n"
                        + (chosen is null ? string.Empty : $"Sec-WebSocket-Protocol: {chosen}\r\n")
                        + "\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(answer));

        using WebSocket socket = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions { IsServer = true, SubProtocol = chosen });
        await serve(socket);
    }

    private static async Task<Dictionary<string, string>> ReadHeadAsync(NetworkStream stream)
    {
        List<byte> head = [];
        byte[] one = new byte[1];

        while (head.Count < 4 || head[^4] != '\r' || head[^3] != '\n' || head[^2] != '\r' || head[^1] != '\n')
        {
            if (await stream.ReadAsync(one) == 0)
            {
                throw new IOException("The connection closed before the request was whole.");
            }

            head.Add(one[0]);
        }

        string[] lines = Encoding.ASCII.GetString([.. head]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase) { ["Request-Line"] = lines[0] };

        foreach (string line in lines.Skip(1))
        {
            string[] pair = line.Split(": ", 2);
            headers[pair[0]] = pair[1];
        }

        return headers;
    }
}

/// <summary>
/// Two ends of one connection on the loopback interface, for a WebSocket on each end.
/// </summary>
internal static class DuplexPipe
{
    public static (Stream One, Stream Other) Pair()
    {
        using Socket listener = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        Socket connecting = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        connecting.Connect(listener.LocalEndPoint!);
        Socket accepted = listener.Accept();

        return (new NetworkStream(accepted, ownsSocket: true), new NetworkStream(connecting, ownsSocket: true));
    }
}
