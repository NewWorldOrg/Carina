using System.Net;
using System.Text.Json;

using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Infrastructure.Driver;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class DriverFeature : IAsyncDisposable
{
    private static readonly Uri StatusPath = new("/api/driver/status", UriKind.Relative);

    private static readonly DriverSupervisionSettings Impatient = new(
        TimeSpan.FromMilliseconds(20),
        TimeSpan.FromMilliseconds(200),
        [DriverCapabilities.Recording, DriverCapabilities.Live],
        () => 1.0);

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
            }))
            .CreateAuthenticatedClient();
    }

    public HttpClient Client { get; }

    public RecordingResyncHook Hook { get; }

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
