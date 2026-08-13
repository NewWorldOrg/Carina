using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;

using Carina.Contracts;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Carina.TestSupport;

public sealed class FakeDriver : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly List<Channel<string>> listeners = [];
    private readonly Lock gate = new();

    private FakeDriver(WebApplication app, string socketPath, DriverHello hello)
    {
        this.app = app;
        SocketPath = socketPath;
        Hello = hello;
    }

    public string SocketPath { get; }

    public DriverHello Hello { get; set; }

    public IReadOnlyList<SessionSnapshot> Sessions { get; set; } = [];

    public IReadOnlyList<TunerSnapshot> Tuners { get; set; } = [];

    public IReadOnlyList<DiagnosticSnapshot> Diagnostics { get; set; } = [];

    public DriverProblem? RefuseEverythingWith { get; set; }

    public int RefusalStatus { get; set; } = StatusCodes.Status503ServiceUnavailable;

    public Dictionary<string, Refusal> RefusalsByPath { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> RawBodyByPath { get; } = new(StringComparer.Ordinal);

    public bool TruncateHealth { get; set; }

    public SemaphoreSlim StreamAbortGate { get; } = new(0);

    public int ListenerCount
    {
        get
        {
            lock (gate)
            {
                return listeners.Count;
            }
        }
    }

    public static DriverHello HelloFor(
        string instanceId,
        bool draining = false,
        string[]? capabilities = null,
        int protocolVersion = DriverProtocol.Version)
        => new(protocolVersion, instanceId, capabilities ?? ["recording", "live"])
        {
            Draining = draining,
        };

    public static async Task<FakeDriver> StartAsync(
        string socketPath,
        DriverHello hello,
        Action<FakeDriver>? arrange = null)
    {
        if (File.Exists(socketPath))
        {
            File.Delete(socketPath);
        }

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, string.Empty);
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.ListenUnixSocket(socketPath));

        var app = builder.Build();
        var driver = new FakeDriver(app, socketPath, hello);
        arrange?.Invoke(driver);
        app.Lifetime.ApplicationStopping.Register(driver.CloseAllListeners);

        app.MapGet(DriverEndpoints.Health, driver.HealthAsync);
        app.MapGet(DriverEndpoints.Tuners, context =>
            driver.CannedAsync(context, driver.Tuners, DriverJson.Context.IReadOnlyListTunerSnapshot));
        app.MapGet(DriverEndpoints.Sessions, context =>
            driver.CannedAsync(context, driver.Sessions, DriverJson.Context.IReadOnlyListSessionSnapshot));
        app.MapGet(DriverEndpoints.Diagnostics, context =>
            driver.CannedAsync(context, driver.Diagnostics, DriverJson.Context.IReadOnlyListDiagnosticSnapshot));
        app.MapPost(DriverEndpoints.Sessions, driver.StartSessionAsync);
        app.MapDelete($"{DriverEndpoints.Sessions}/{{id}}", driver.StopSessionAsync);
        app.MapGet($"{DriverEndpoints.Sessions}/{{id}}/stream", driver.AbortedStreamAsync);
        app.MapGet(DriverEndpoints.Events, driver.EventsAsync);

        await app.StartAsync();

        return driver;
    }

    public void Signal(string name)
    {
        lock (gate)
        {
            foreach (var listener in listeners)
            {
                listener.Writer.TryWrite(name);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await app.StopAsync(patience.Token);
        await app.DisposeAsync();
    }

    private void CloseAllListeners()
    {
        lock (gate)
        {
            foreach (var listener in listeners)
            {
                listener.Writer.TryComplete();
            }
        }
    }

    private async Task HealthAsync(HttpContext context)
    {
        if (await HandledAsync(context))
        {
            return;
        }

        if (TruncateHealth)
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"protocolVersion\":", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            context.Abort();

            return;
        }

        await WriteAsync(context, StatusCodes.Status200OK, Hello, DriverJson.Context.DriverHello);
    }

    private async Task CannedAsync<T>(HttpContext context, T value, JsonTypeInfo<T> typeInfo)
    {
        if (await HandledAsync(context))
        {
            return;
        }

        await WriteAsync(context, StatusCodes.Status200OK, value, typeInfo);
    }

    private async Task StartSessionAsync(HttpContext context)
    {
        if (await HandledAsync(context))
        {
            return;
        }

        var request = await context.Request.ReadFromJsonAsync(
            DriverJson.Context.StartSessionRequest,
            context.RequestAborted);

        var snapshot = new SessionSnapshot(
            request!.SessionId,
            request.Purpose,
            request.DeviceId ?? "fake-terrestrial",
            SessionState.Active,
            DateTimeOffset.UtcNow,
            request.EndsAt);

        await WriteAsync(
            context,
            StatusCodes.Status201Created,
            snapshot,
            DriverJson.Context.SessionSnapshot);
    }

    private async Task StopSessionAsync(HttpContext context)
    {
        if (await HandledAsync(context))
        {
            return;
        }

        context.Response.StatusCode = StatusCodes.Status202Accepted;
    }

    private async Task AbortedStreamAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "video/mp2t";
        await context.Response.Body.WriteAsync(new byte[188], context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
        await StreamAbortGate.WaitAsync(context.RequestAborted);
        context.Abort();
    }

    private async Task EventsAsync(HttpContext context)
    {
        if (await HandledAsync(context))
        {
            return;
        }

        var channel = Channel.CreateUnbounded<string>();

        lock (gate)
        {
            listeners.Add(channel);
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream";

        try
        {
            await context.Response.StartAsync(context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);

            await foreach (var name in channel.Reader.ReadAllAsync(context.RequestAborted))
            {
                await context.Response.WriteAsync(
                    $"event: {name}\ndata: {name}\n\n",
                    context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            lock (gate)
            {
                listeners.Remove(channel);
            }
        }
    }

    private async Task<bool> HandledAsync(HttpContext context)
    {
        var path = context.Request.Path.Value;

        if (RefusalFor(path) is { } refusal)
        {
            await WriteAsync(
                context,
                refusal.Status,
                refusal.Problem,
                DriverJson.Context.DriverProblem);

            return true;
        }

        if (path is null || !RawBodyByPath.TryGetValue(path, out var body))
        {
            return false;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(body, context.RequestAborted);

        return true;
    }

    private Refusal? RefusalFor(string? path)
    {
        if (path is not null && RefusalsByPath.TryGetValue(path, out var refusal))
        {
            return refusal;
        }

        return RefuseEverythingWith is { } problem ? new Refusal(RefusalStatus, problem) : null;
    }

    private static Task WriteAsync<T>(
        HttpContext context,
        int status,
        T value,
        JsonTypeInfo<T> typeInfo)
    {
        context.Response.StatusCode = status;

        return context.Response.WriteAsJsonAsync(
            value,
            typeInfo,
            contentType: null,
            cancellationToken: context.RequestAborted);
    }

    public sealed record Refusal(int Status, DriverProblem Problem);
}
