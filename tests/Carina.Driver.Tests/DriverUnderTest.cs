using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json.Serialization.Metadata;

using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Ipc;

using Microsoft.Extensions.Hosting;

namespace Carina.Driver.Tests;

public sealed class DriverUnderTest : IAsyncDisposable
{
    private readonly IHost host;
    private readonly string root;

    private DriverUnderTest(IHost host, string root, DriverConfiguration configuration)
    {
        this.host = host;
        this.root = root;
        Configuration = configuration;
    }

    public DriverConfiguration Configuration { get; }

    public string SocketPath => Configuration.SocketPath!;

    public static DriverConfiguration ConfigurationIn(string root)
    {
        var recordings = Path.Combine(root, "recordings");
        Directory.CreateDirectory(recordings);

        return new DriverConfiguration(
            Path.Combine(root, "driver.sock"),
            [new OutputRootSettings("primary", recordings)],
            6,
            new TunerSettings(TunerBackend.Fake),
            [
                new DeviceSettings("fake-terrestrial", DeviceKind.Terrestrial),
                new DeviceSettings("fake-satellite", DeviceKind.Satellite),
                new DeviceSettings("fake-spare", DeviceKind.Terrestrial, Enabled: false),
            ],
            SocketGroupId: (int)UnixFile.CurrentGroupId()
        );
    }

    public static string NewRoot() =>
        Directory.CreateTempSubdirectory("carina-driver-").FullName;

    public static async Task<DriverUnderTest> Start(string[]? args = null)
    {
        ClearTheInheritedUrls();

        var root = NewRoot();
        var configuration = ConfigurationIn(root);
        var built = DriverHost.Create(args ?? [], configuration);

        Assert.True(built.TryGetHost(out var host), string.Join(" ", built.Problems));

        await host.StartAsync();

        return new DriverUnderTest(host, root, configuration);
    }

    public static void ClearTheInheritedUrls()
    {
        Environment.SetEnvironmentVariable(TcpBindingGate.UrlsVariable, null);
        Environment.SetEnvironmentVariable("DOTNET_URLS", null);
        Environment.SetEnvironmentVariable("URLS", null);
    }

    public T Service<T>()
        where T : notnull => (T)host.Services.GetService(typeof(T))!;

    public HttpClient Client()
    {
        var path = SocketPath;

        return new HttpClient(
            new SocketsHttpHandler
            {
                ConnectCallback = async (_, cancellationToken) =>
                {
                    var socket = new Socket(
                        AddressFamily.Unix,
                        SocketType.Stream,
                        ProtocolType.Unspecified
                    );

                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), cancellationToken);

                    return new NetworkStream(socket, ownsSocket: true);
                },
            }
        )
        {
            BaseAddress = new Uri("http://driver"),
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    public static async Task<T?> Read<T>(HttpResponseMessage response, JsonTypeInfo<T> typeInfo) =>
        await response.Content.ReadFromJsonAsync(typeInfo);

    public static HttpContent Body(StartSessionRequest request) =>
        JsonContent.Create(request, DriverJson.Context.StartSessionRequest);

    public static StartSessionRequest Live(string sessionId, string? deviceId = null) =>
        new()
        {
            SessionId = SessionId.Parse(sessionId),
            Purpose = SessionPurpose.Live,
            Tuning = new TuningRequest(TunerKind.Terrestrial, 27),
            DeviceId = deviceId,
        };

    public static StartSessionRequest Recording(
        string sessionId,
        DateTimeOffset endsAt,
        string outputRoot = "primary"
    ) =>
        new()
        {
            SessionId = SessionId.Parse(sessionId),
            Purpose = SessionPurpose.Recording,
            Tuning = new TuningRequest(TunerKind.Terrestrial, 27),
            OutputRoot = outputRoot,
            EndsAt = endsAt,
        };

    public async ValueTask DisposeAsync()
    {
        try
        {
            await host.StopAsync(TimeSpan.FromSeconds(20));
        }
        finally
        {
            host.Dispose();

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
