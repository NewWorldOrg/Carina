using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

using Carina.Infrastructure.DataBroadcast;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Tests.DataBroadcast;

internal sealed record HeardRequest(string Method, string Target, IReadOnlyDictionary<string, string> Headers, byte[] Body);

internal sealed record CannedAnswer(int Status, byte[] Body, IReadOnlyList<KeyValuePair<string, string>> Headers, bool Measured = true)
{
    public static CannedAnswer Of(int status, byte[] body, params string[] headers)
        => new(status, body, [.. headers.Select(header => header.Split(": ", 2)).Select(pair => KeyValuePair.Create(pair[0], pair[1]))]);

    public static CannedAnswer Text(int status, string body) => Of(status, Encoding.ASCII.GetBytes(body), "Content-Type: text/plain");
}

/// <summary>
/// A broadcaster's server on the loopback interface, answering each request the way it is told to and keeping what
/// it heard, over plain HTTP/1.1 or, given a certificate, over TLS.
/// </summary>
internal sealed class BroadcasterServerStandIn : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);

    private readonly Func<HeardRequest, Task<CannedAnswer>> answer;

    private readonly X509Certificate2? certificate;

    private readonly CancellationTokenSource stopping = new();

    private readonly Task accepting;

    public BroadcasterServerStandIn(Func<HeardRequest, Task<CannedAnswer>> answer, X509Certificate2? certificate = null)
    {
        this.answer = answer;
        this.certificate = certificate;
        listener.Start();
        accepting = AcceptAsync();
    }

    public BroadcasterServerStandIn(Func<HeardRequest, CannedAnswer> answer, X509Certificate2? certificate = null)
        : this(request => Task.FromResult(answer(request)), certificate)
    {
    }

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public ConcurrentQueue<HeardRequest> Heard { get; } = new();

    public static X509Certificate2 SelfSigned(string host)
    {
        using RSA key = RSA.Create(2048);
        CertificateRequest request = new($"CN={host}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        SubjectAlternativeNameBuilder names = new();
        names.AddDnsName(host);
        request.CertificateExtensions.Add(names.Build());

        using X509Certificate2 made = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        return X509CertificateLoader.LoadPkcs12(made.Export(X509ContentType.Pfx), null);
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
            _ = Task.Run(() => ServeAsync(connection));
        }
    }

    private async Task ServeAsync(TcpClient connection)
    {
        using TcpClient held = connection;

        try
        {
            await using Stream stream = await OpenAsync(held.GetStream());
            HeardRequest request = await ReadAsync(stream);
            Heard.Enqueue(request);
            CannedAnswer canned = await answer(request).WaitAsync(stopping.Token);
            await WriteAsync(stream, canned);
        }
        catch (Exception failure) when (failure is IOException or OperationCanceledException or System.Security.Authentication.AuthenticationException)
        {
        }
    }

    private async Task<Stream> OpenAsync(NetworkStream plain)
    {
        if (certificate is null)
        {
            return plain;
        }

        SslStream secured = new(plain, leaveInnerStreamOpen: false);
        await secured.AuthenticateAsServerAsync(certificate, false, false);

        return secured;
    }

    private static async Task<HeardRequest> ReadAsync(Stream stream)
    {
        List<byte> head = [];
        byte[] one = new byte[1];

        while (!EndsTheHead(head))
        {
            if (await stream.ReadAsync(one) == 0)
            {
                throw new IOException("The connection closed before the request was whole.");
            }

            head.Add(one[0]);
        }

        string[] lines = Encoding.ASCII.GetString([.. head]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        string[] start = lines[0].Split(' ');
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);

        foreach (string line in lines.Skip(1))
        {
            string[] pair = line.Split(": ", 2);
            headers[pair[0]] = pair[1];
        }

        byte[] body = new byte[headers.TryGetValue("Content-Length", out string? length) ? int.Parse(length, System.Globalization.CultureInfo.InvariantCulture) : 0];
        await stream.ReadExactlyAsync(body);

        return new HeardRequest(start[0], start[1], headers, body);
    }

    private static bool EndsTheHead(List<byte> head)
        => head.Count >= 4 && head[^4] == '\r' && head[^3] == '\n' && head[^2] == '\r' && head[^1] == '\n';

    private static async Task WriteAsync(Stream stream, CannedAnswer canned)
    {
        StringBuilder head = new($"HTTP/1.1 {canned.Status} Answered\r\nConnection: close\r\n");

        if (canned.Measured)
        {
            head.Append($"Content-Length: {canned.Body.Length}\r\n");
        }

        foreach (KeyValuePair<string, string> header in canned.Headers)
        {
            head.Append($"{header.Key}: {header.Value}\r\n");
        }

        head.Append("\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()));
        await stream.WriteAsync(canned.Body);
        await stream.FlushAsync();
    }
}

/// <summary>
/// The network as the relay sees it in a test: names resolve to the addresses written here, an unknown name does
/// not resolve, the machine answers at the addresses written as its own, and every connection, to whichever address,
/// reaches the stand-in on the loopback interface.
/// </summary>
internal sealed class PointedNetwork(int port) : IRelayNetwork
{
    public Dictionary<string, IPAddress[]> Names { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<IPAddress> Mine { get; } = [];

    public ConcurrentQueue<string> Resolved { get; } = new();

    public ConcurrentQueue<IPEndPoint> Connected { get; } = new();

    public bool Refuses { get; set; }

    public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        Resolved.Enqueue(host);

        return Names.TryGetValue(host, out IPAddress[]? addresses)
            ? Task.FromResult<IReadOnlyList<IPAddress>>(addresses)
            : Task.FromException<IReadOnlyList<IPAddress>>(new SocketException((int)SocketError.HostNotFound));
    }

    public IReadOnlyCollection<IPAddress> OwnAddresses() => Mine;

    public async Task<Stream> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        Connected.Enqueue(endpoint);

        if (Refuses)
        {
            throw new SocketException((int)SocketError.ConnectionRefused);
        }

        Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port), cancellationToken);

        return new NetworkStream(socket, ownsSocket: true);
    }
}

internal sealed class KeptRelayLog : ILogger<BroadcasterRelay>
{
    public ConcurrentQueue<KeptLine> Lines { get; } = new();

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        IReadOnlyDictionary<string, object?> values = state is IEnumerable<KeyValuePair<string, object?>> pairs
            ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            : new Dictionary<string, object?>();

        Lines.Enqueue(new KeptLine(logLevel, formatter(state, exception), values));
    }
}

internal sealed record KeptLine(LogLevel Level, string Said, IReadOnlyDictionary<string, object?> Values);
