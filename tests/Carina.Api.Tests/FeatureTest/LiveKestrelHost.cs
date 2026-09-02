using System.Net.WebSockets;
using System.Threading.Channels;

using Carina.Api.Live;
using Carina.Domain.Streaming;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class LiveKestrelHost : IAsyncDisposable
{
    private readonly WebApplication app;

    private readonly ILiveWireSource source;

    private readonly LiveWireSettings settings;

    private readonly Channel<LiveDeparture> departures = Channel.CreateUnbounded<LiveDeparture>();

    private LiveKestrelHost(WebApplication app, ILiveWireSource source, LiveWireSettings settings)
    {
        this.app = app;
        this.source = source;
        this.settings = settings;
        Wire = new Uri("ws://localhost" + LiveWire.Path);
    }

    public Uri Wire { get; private set; }

    public static async Task<LiveKestrelHost> StartAsync(ILiveWireSource source, LiveWireSettings? settings = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, "http://127.0.0.1:0");

        WebApplication app = builder.Build();

        var host = new LiveKestrelHost(app, source, settings ?? new LiveWireSettings());

        app.UseWebSockets();
        app.MapGet(LiveWire.Path, host.CarryAsync);

        await app.StartAsync();

        host.Wire = host.ResolveWire();

        return host;
    }

    public async Task<LiveDeparture> DepartureAsync(CancellationToken cancellationToken)
        => await departures.Reader.ReadAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }

    private async Task CarryAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;

            return;
        }

        ILiveViewing? viewing = await source.JoinAsync(context.RequestAborted);

        if (viewing is null)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;

            return;
        }

        await using (viewing)
        {
            using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();

            LiveDeparture departure = await new LiveWireSocket(socket, settings).CarryAsync(
                viewing.Frames,
                CancellationToken.None,
                context.RequestAborted);

            departures.Writer.TryWrite(departure);
        }
    }

    private Uri ResolveWire()
    {
        string address = app.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()!
            .Addresses
            .First();

        var http = new Uri(address);

        return new Uri($"ws://{http.Host}:{http.Port}{LiveWire.Path}");
    }
}
