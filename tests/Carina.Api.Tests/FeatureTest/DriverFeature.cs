using System.Net;
using System.Text.Json;

using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Driver;
using Carina.TestSupport;

using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Puppis.Contracts;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class DriverFeature : IAsyncDisposable
{
    private static readonly Uri StatusPath = new("/api/driver/status", UriKind.Relative);

    private static readonly DriverSupervisionSettings Impatient = new(
        TimeSpan.FromMilliseconds(20),
        TimeSpan.FromMilliseconds(200),
        [DriverCapabilities.Recording, DriverCapabilities.Live],
        () => 1.0)
    {
        DrainPoll = TimeSpan.FromMilliseconds(20),
    };

    private readonly TempSocket socket = new();
    private readonly TestingWebApplicationFactory factory;
    private FakeDriver? driver;

    private DriverFeature()
    {
        factory = new TestingWebApplicationFactory { DriverSocketPath = socket.Path };
        Hook = new RecordingResyncHook();
        Client = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IDriverSessionResyncHook>(Hook);
                services.AddSingleton(Impatient);
                services.AddSingleton<ICandidateChannelRepository>(Candidates);
                services.AddSingleton<IServiceReachSettingsRepository>(ReachSettings);
                services.AddSingleton<IRecalculationNotice>(Notices);
                services.AddSingleton<IRecordingTitles>(Titles);
            }))
            .CreateAuthenticatedClient();
    }

    public HttpClient Client { get; }

    public RecordingResyncHook Hook { get; }

    public HeldCandidates Candidates { get; } = new();

    public HeldReachSettings ReachSettings { get; } = new();

    public CountedNotices Notices { get; } = new();

    public HeldRecordingTitles Titles { get; } = new();

    public FakeDriver Driver => driver
        ?? throw new InvalidOperationException("No driver double is running.");

    public static async Task<DriverFeature> StartAsync(
        DriverHello? hello = null,
        Action<FakeDriver>? arrange = null)
    {
        var feature = new DriverFeature();

        if (hello is not null)
        {
            await feature.StartDriverAsync(hello, arrange);
        }

        return feature;
    }

    public static string ConnectionOf(JsonElement data)
        => data.GetProperty("connection").GetString()!;

    public static async Task<(HttpStatusCode Status, JsonElement Body)> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return (response.StatusCode, document.RootElement.Clone());
    }

    public async Task StartDriverAsync(DriverHello hello, Action<FakeDriver>? arrange = null)
    {
        await StopDriverAsync();

        driver = await FakeDriver.StartAsync(socket.Path, hello, arrange);
    }

    public async Task StopDriverAsync()
    {
        if (driver is { } running)
        {
            driver = null;
            await running.DisposeAsync();
        }
    }

    public async Task<JsonElement> StatusAsync()
    {
        using HttpResponseMessage response = await Client.GetAsync(StatusPath);
        string payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(payload);

        Assert.True(body.RootElement.GetProperty("status").GetBoolean());

        return body.RootElement.GetProperty("data").Clone();
    }

    public Task<JsonElement> UntilConnectionIs(string connection)
        => Eventually.Yields(
            StatusAsync,
            data => ConnectionOf(data) == connection,
            ConnectionOf,
            $"the status endpoint reports {connection}");

    public Task UntilReadoptions(int count)
        => Eventually.Happens(
            () => Hook.CallCount == count,
            $"the resync hook has been called {count} time(s)");

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await StopDriverAsync();
        await factory.DisposeAsync();
        socket.Dispose();
    }
}
