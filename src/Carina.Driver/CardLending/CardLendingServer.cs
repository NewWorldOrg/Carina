using System.Net;
using System.Net.Sockets;

using Carina.Driver.Descrambling;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Driver.CardLending;

/// <summary>
/// Where and to whom this driver lends its card.
/// </summary>
public sealed record LendingPlan(
    IPEndPoint Listen,
    IReadOnlyList<string> HostNames,
    int HopLimit,
    TimeSpan? RoundTrip,
    int Leases,
    LendingKey Key
);

/// <summary>
/// The lending port: a server of its own, apart from the one on the driver's socket, that serves the three lending
/// requests and nothing else. A connection from outside the household is closed before it is read, and a request
/// naming a host the lender is not called, or carrying the marks of a proxy or a browser, is not answered.
/// </summary>
public sealed class CardLendingServer : IHostedService, IDisposable
{
    public const int SpareConnections = 4;

    private static readonly string[] HeadersNotAnswered =
    [
        "Forwarded",
        "X-Forwarded-For",
        "X-Forwarded-Host",
        "X-Forwarded-Proto",
        "X-Real-IP",
        "Via",
        "CF-Connecting-IP",
        "CF-Ray",
        "True-Client-IP",
        "Origin",
    ];

    private readonly LendingPlan plan;

    private readonly ILogger? logger;

    private WebApplication? app;

    public CardLendingServer(
        LendingPlan plan,
        Func<IConditionalAccess> openCard,
        TimeProvider time,
        ILogger? logger
    )
    {
        ArgumentNullException.ThrowIfNull(plan);

        this.plan = plan;
        this.logger = logger;
        Lender = new CardLender(plan.Key, openCard, new CardLendingLimits(plan.Leases, plan.RoundTrip), time, logger);
    }

    public CardLender Lender { get; }

    /// <summary>
    /// The address the port was bound to once the server started.
    /// </summary>
    public IPEndPoint? Bound { get; private set; }

    /// <summary>
    /// A bound listening socket whose hop limit every connection it accepts inherits, the SYN-ACK included.
    /// </summary>
    public static Socket ListenSocket(EndPoint endpoint, int hops)
    {
        Socket socket = SocketTransportOptions.CreateDefaultBoundListenSocket(endpoint);

        try
        {
            HopLimit.Apply(socket, hops);
        }
        catch
        {
            socket.Dispose();

            throw;
        }

        return socket;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, string.Empty);
        builder.WebHost.UseSockets(options =>
            options.CreateBoundListenSocket = endpoint => ListenSocket(endpoint, plan.HopLimit)
        );
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = LendingProtocol.LongestBody;
            options.Limits.MaxRequestHeaderCount = 32;
            options.Limits.MaxRequestHeadersTotalSize = 8 * 1024;
            options.Limits.MaxRequestLineSize = 1024;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5);
            options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
            options.Limits.MaxConcurrentConnections = plan.Leases + SpareConnections;
            options.Listen(
                plan.Listen,
                listen =>
                {
                    listen.Protocols = HttpProtocols.Http1;
                    listen.Use(next => connection => AdmitAsync(connection, next));
                }
            );
        });

        WebApplication built = builder.Build();
        built.Use(Gate);
        built.MapPost(LendingProtocol.ChallengePath, ChallengeAsync);
        built.MapPost(LendingProtocol.LeasePath, LeaseAsync);
        built.MapPost(LendingProtocol.EcmPath, EcmAsync);

        await built.StartAsync(cancellationToken);
        app = built;
        Bound = BoundEndPoint(built);

        logger?.LogInformation(
            "This driver lends its card on {Address} to borrowers inside the household that hold the key, under the names {Names}.",
            Bound,
            string.Join(", ", plan.HostNames)
        );
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (app is not null)
        {
            await app.StopAsync(cancellationToken);
        }

        Lender.Dispose();
    }

    private async Task AdmitAsync(ConnectionContext connection, ConnectionDelegate next)
    {
        if (connection.RemoteEndPoint is not IPEndPoint peer || !HomeNetwork.Holds(peer.Address))
        {
            logger?.LogWarning(
                "A connection to the lending port from {Peer}, which is not inside the household, was closed unread.",
                connection.RemoteEndPoint
            );

            return;
        }

        if (connection.Features.Get<IConnectionSocketFeature>()?.Socket is Socket socket)
        {
            HopLimit.Apply(socket, plan.HopLimit);
        }

        try
        {
            await next(connection);
        }
        finally
        {
            Lender.Release(connection.ConnectionId);
        }
    }

    private Task Gate(HttpContext context, RequestDelegate next)
    {
        string host = context.Request.Host.Host.Trim('[', ']');

        if (!plan.HostNames.Contains(host, StringComparer.OrdinalIgnoreCase))
        {
            return Unanswered(context, "named a host this driver is not called");
        }

        if (HeadersNotAnswered.Any(context.Request.Headers.ContainsKey))
        {
            return Unanswered(context, "came through a proxy or from a browser");
        }

        return next(context);
    }

    private Task Unanswered(HttpContext context, string why)
    {
        logger?.LogWarning(
            "A request to the lending port from {Peer} {Why}, so it was not answered.",
            context.Connection.RemoteIpAddress,
            why
        );
        context.Abort();

        return Task.CompletedTask;
    }

    private Task ChallengeAsync(HttpContext context) =>
        WriteAsync(context, new LendingReply(LendingReply.Ok, Lender.Challenge(context.Connection.Id)));

    private async Task LeaseAsync(HttpContext context)
    {
        byte[]? body = await ReadAsync(context.Request, context.RequestAborted);

        await WriteAsync(
            context,
            body is null
                ? LendingReply.Refused(LendingReply.Malformed, "malformed")
                : Lender.Lease(
                    context.Connection.Id,
                    context.Request.Host.Host.Trim('[', ']'),
                    body,
                    context.Connection.RemoteIpAddress?.ToString()
                )
        );
    }

    private async Task EcmAsync(HttpContext context)
    {
        byte[]? body = await ReadAsync(context.Request, context.RequestAborted);

        await WriteAsync(
            context,
            body is null
                ? LendingReply.Refused(LendingReply.Malformed, "malformed")
                : Lender.Answer(context.Connection.Id, body)
        );
    }

    private static async Task<byte[]?> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using MemoryStream body = new();
        byte[] buffer = new byte[LendingProtocol.LongestBody];
        int read;

        while ((read = await request.Body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (body.Length + read > LendingProtocol.LongestBody)
            {
                return null;
            }

            body.Write(buffer, 0, read);
        }

        return body.ToArray();
    }

    private static async Task WriteAsync(HttpContext context, LendingReply reply)
    {
        context.Response.StatusCode = reply.Status;
        context.Response.ContentType = "application/octet-stream";
        context.Response.ContentLength = reply.Body.Length;
        await context.Response.Body.WriteAsync(reply.Body, context.RequestAborted);
    }

    private static IPEndPoint? BoundEndPoint(WebApplication built)
    {
        string? address = built
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()
            ?.Addresses.FirstOrDefault();

        if (address is null)
        {
            return null;
        }

        Uri uri = new(address);

        return IPEndPoint.Parse($"{uri.Host}:{uri.Port}");
    }

    public void Dispose()
    {
        Lender.Dispose();
        (app as IDisposable)?.Dispose();
    }
}
