using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Domain.DriverStatus;
using Carina.Infrastructure.Configuration;

using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Driver;

public sealed class DriverIpcClient : IDriverClient, IDisposable
{
    public static readonly TimeSpan RequestPatience = TimeSpan.FromSeconds(10);

    private readonly HttpClient http;

    public DriverIpcClient(IOptions<DriverOptions> driverOptions)
    {
        var socketPath = new DriverSocketPath(driverOptions.Value.SocketPath!);

        http = new HttpClient(
            new SocketsHttpHandler
            {
                ConnectCallback = async (_, cancellationToken) =>
                {
                    var socket = new Socket(
                        AddressFamily.Unix,
                        SocketType.Stream,
                        ProtocolType.Unspecified);

                    try
                    {
                        await socket.ConnectAsync(
                            new UnixDomainSocketEndPoint(socketPath.Value),
                            cancellationToken);
                    }
                    catch
                    {
                        socket.Dispose();

                        throw;
                    }

                    return new NetworkStream(socket, ownsSocket: true);
                },
            })
        {
            BaseAddress = new Uri("http://driver"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    public Task<DriverCall<DriverHello>> GetHealthAsync(CancellationToken cancellationToken)
        => GetAsync(DriverEndpoints.Health, DriverJson.Context.DriverHello, cancellationToken);

    public Task<DriverCall<IReadOnlyList<TunerSnapshot>>> GetTunersAsync(
        CancellationToken cancellationToken)
        => GetAsync(
            DriverEndpoints.Tuners,
            DriverJson.Context.IReadOnlyListTunerSnapshot,
            cancellationToken);

    public Task<DriverCall<IReadOnlyList<SessionSnapshot>>> GetActiveSessionsAsync(
        CancellationToken cancellationToken)
        => GetAsync(
            DriverEndpoints.Sessions,
            DriverJson.Context.IReadOnlyListSessionSnapshot,
            cancellationToken);

    public Task<DriverCall<SessionSnapshot>> GetSessionAsync(
        SessionId sessionId,
        CancellationToken cancellationToken)
        => GetAsync(
            DriverEndpoints.Session(sessionId),
            DriverJson.Context.SessionSnapshot,
            cancellationToken);

    public Task<DriverCall<IReadOnlyList<DiagnosticSnapshot>>> GetDiagnosticsAsync(
        CancellationToken cancellationToken)
        => GetAsync(
            DriverEndpoints.Diagnostics,
            DriverJson.Context.IReadOnlyListDiagnosticSnapshot,
            cancellationToken);

    public async Task<DriverCall<SessionSnapshot>> StartSessionAsync(
        StartSessionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var patience = Patience(cancellationToken);
            using var body = JsonContent.Create(request, DriverJson.Context.StartSessionRequest);
            using var response = await http.PostAsync(DriverEndpoints.Sessions, body, patience.Token);

            return await ReadAsync(
                response,
                DriverJson.Context.SessionSnapshot,
                bodyRequired: true,
                patience.Token);
        }
        catch (Exception error) when (IsTransport(error, cancellationToken))
        {
            return DriverCall<SessionSnapshot>.Unreachable(Describe(error));
        }
    }

    public async Task<DriverCall<SessionSnapshot>> StopSessionAsync(
        SessionId sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var patience = Patience(cancellationToken);
            using var response = await http.DeleteAsync(
                DriverEndpoints.Session(sessionId),
                patience.Token);

            return await ReadAsync(
                response,
                DriverJson.Context.SessionSnapshot,
                bodyRequired: false,
                patience.Token);
        }
        catch (Exception error) when (IsTransport(error, cancellationToken))
        {
            return DriverCall<SessionSnapshot>.Unreachable(Describe(error));
        }
    }

    public Task<DriverCall<Stream>> OpenSessionStreamAsync(
        SessionId sessionId,
        string? subscriber,
        CancellationToken cancellationToken)
    {
        var path = DriverEndpoints.SessionStream(sessionId);
        var target = subscriber is null
            ? path
            : $"{path}?{DriverEndpoints.SubscriberQuery}={Uri.EscapeDataString(subscriber)}";

        return OpenAsync(target, cancellationToken);
    }

    public Task<DriverCall<Stream>> OpenEventsAsync(CancellationToken cancellationToken)
        => OpenAsync(DriverEndpoints.Events, cancellationToken);

    public void Dispose() => http.Dispose();

    private async Task<DriverCall<T>> GetAsync<T>(
        string path,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            using var patience = Patience(cancellationToken);
            using var response = await http.GetAsync(path, patience.Token);

            return await ReadAsync(response, typeInfo, bodyRequired: true, patience.Token);
        }
        catch (Exception error) when (IsTransport(error, cancellationToken))
        {
            return DriverCall<T>.Unreachable(Describe(error));
        }
    }

    private async Task<DriverCall<Stream>> OpenAsync(string path, CancellationToken cancellationToken)
    {
        var patience = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var handedOver = false;

        try
        {
            patience.CancelAfter(RequestPatience);

            var response = await http.GetAsync(
                path,
                HttpCompletionOption.ResponseHeadersRead,
                patience.Token);

            if (!response.IsSuccessStatusCode)
            {
                using (response)
                {
                    return DriverCall<Stream>.Refused(await ProblemIn(response, patience.Token));
                }
            }

            patience.CancelAfter(Timeout.InfiniteTimeSpan);

            var stream = await response.Content.ReadAsStreamAsync(patience.Token);
            handedOver = true;

            return DriverCall<Stream>.Reached(new OwnedStream(stream, response, patience));
        }
        catch (Exception error) when (IsTransport(error, cancellationToken))
        {
            return DriverCall<Stream>.Unreachable(Describe(error));
        }
        finally
        {
            if (!handedOver)
            {
                patience.Dispose();
            }
        }
    }

    private static async Task<DriverCall<T>> ReadAsync<T>(
        HttpResponseMessage response,
        JsonTypeInfo<T> typeInfo,
        bool bodyRequired,
        CancellationToken cancellationToken)
        where T : class
    {
        var status = (int)response.StatusCode;

        if (!response.IsSuccessStatusCode)
        {
            return DriverCall<T>.Refused(await ProblemIn(response, cancellationToken));
        }

        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        if (body.Length == 0)
        {
            return bodyRequired
                ? DriverCall<T>.Unreachable(
                    $"The driver answered {status} with no body for a call that needs one.")
                : DriverCall<T>.Reached(null);
        }

        var value = JsonSerializer.Deserialize(body, typeInfo);

        return value is null
            ? DriverCall<T>.Unreachable($"The driver answered {status} with a body that reads as nothing.")
            : DriverCall<T>.Reached(value);
    }

    private static async Task<DriverProblem> ProblemIn(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            if (body.Length > 0)
            {
                var problem = JsonSerializer.Deserialize(body, DriverJson.Context.DriverProblem);

                if (problem is not null)
                {
                    return problem;
                }
            }
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException)
        {
        }

        return new DriverProblem($"http{(int)response.StatusCode}", []);
    }

    private static CancellationTokenSource Patience(CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(RequestPatience);

        return source;
    }

    private static bool IsTransport(Exception error, CancellationToken cancellationToken)
        => !cancellationToken.IsCancellationRequested
           && error is HttpRequestException
               or SocketException
               or IOException
               or JsonException
               or OperationCanceledException;

    private static string Describe(Exception error)
        => error is OperationCanceledException
            ? $"The driver did not answer within {RequestPatience.TotalSeconds:0} seconds."
            : $"{error.GetType().Name}: {error.Message}";

    private sealed class OwnedStream(
        Stream inner,
        HttpResponseMessage response,
        CancellationTokenSource patience) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
            => inner.Read(buffer, offset, count);

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
            => inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
            => inner.ReadAsync(buffer, cancellationToken);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                response.Dispose();
                patience.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
