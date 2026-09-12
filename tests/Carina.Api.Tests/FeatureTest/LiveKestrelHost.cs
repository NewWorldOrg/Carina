using System.Threading.Channels;

using Carina.Api.Live;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class LiveKestrelHost : IAsyncDisposable
{
    private const string Handshake = "?network=32736&service=1024&profile=720p30";

    private readonly WebApplication app;

    private readonly SeatingAt seating;

    private readonly LiveWireSettings settings;

    private readonly Channel<LiveDeparture> departures = Channel.CreateUnbounded<LiveDeparture>();

    private readonly NotedInto noted;

    private LiveKestrelHost(WebApplication app, ILiveWireSource source, LiveWireSettings settings)
    {
        this.app = app;
        this.settings = settings;
        seating = new SeatingAt(source);
        noted = new NotedInto(
            departures,
            new LiveDepartureLedger(TimeProvider.System, NullLogger<LiveDepartureLedger>.Instance));
        Wire = new Uri("ws://localhost" + LiveWire.Path + Handshake);
    }

    public Uri Wire { get; private set; }

    public LiveDepartureTally Tally => noted.Read();

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

    private Task CarryAsync(HttpContext context)
        => LiveWire.Invoke(context, seating, noted, settings, app.Lifetime, TimeProvider.System);

    private Uri ResolveWire()
    {
        string address = app.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()!
            .Addresses
            .First();

        var http = new Uri(address);

        return new Uri($"ws://{http.Host}:{http.Port}{LiveWire.Path}{Handshake}");
    }

    private sealed class NotedInto(Channel<LiveDeparture> departures, ILiveDepartureLedger kept) : ILiveDepartureLedger
    {
        public void Note(LiveSessionKey key, LiveDeparture departure, TimeSpan carried)
        {
            kept.Note(key, departure, carried);
            departures.Writer.TryWrite(departure);
        }

        public LiveDepartureTally Read() => kept.Read();
    }
}
