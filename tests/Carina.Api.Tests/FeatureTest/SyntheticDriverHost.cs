extern alias driver;

using System.Runtime.Versioning;

using Carina.Contracts;

using driver::Carina.Driver;
using driver::Carina.Driver.Configuration;
using driver::Carina.Driver.Ipc;
using driver::Carina.Driver.Sessions;
using driver::Carina.Driver.Transport;
using driver::Carina.Driver.Tuning;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.FeatureTest;

[SupportedOSPlatform("linux")]
internal sealed class PacedTunerDevice(ITunerDevice carrying, TimeSpan between) : ITunerDevice
{
    public long Overflows => carrying.Overflows;

    public ISignalQualitySource? Quality => carrying.Quality;

    public byte[] Read(int count, CancellationToken cancellationToken)
    {
        cancellationToken.WaitHandle.WaitOne(between);
        cancellationToken.ThrowIfCancellationRequested();

        return carrying.Read(count, cancellationToken);
    }

    public void Dispose() => carrying.Dispose();
}

[SupportedOSPlatform("linux")]
internal sealed class PacedTuners(TimeSpan between) : ITunerDeviceFactory
{
    public ITunerDevice Create(DeviceSettings device, TuningRequest tuning, TuneParams? tune)
    {
        TuningRequest asked = tune?.ToLegacyRequest() ?? tuning;

        return new PacedTunerDevice(
            new FakeTunerDevice(asked.PhysicalChannel, asked.ServiceId),
            between);
    }
}

internal sealed record HeldSession(SessionState State, SessionStopReason StopReason, long BytesRecorded);

[SupportedOSPlatform("linux")]
internal sealed class SyntheticDriverHost : IAsyncDisposable
{
    public const string RootName = "primary";

    public static readonly long AChunkOrThree = TunerSession.DefaultChunkSize * 3L;

    private static readonly TimeSpan BetweenReads = TimeSpan.FromMilliseconds(25);

    private static readonly string[] SettingsThatWouldBindAPort =
    [
        .. TcpBindingGate.Variables,
        "DOTNET_URLS",
        "URLS",
    ];

    private readonly string root;
    private readonly string ledger;
    private readonly IReadOnlyList<string?> inherited;
    private readonly Action<IServiceCollection>? reshape;

    private DriverConfiguration configuration;
    private IHost? host;
    private Task? stopping;

    private SyntheticDriverHost(
        IHost host,
        string root,
        string ledger,
        DriverConfiguration configuration,
        IReadOnlyList<string?> inherited,
        Action<IServiceCollection>? reshape)
    {
        this.host = host;
        this.root = root;
        this.ledger = ledger;
        this.configuration = configuration;
        this.inherited = inherited;
        this.reshape = reshape;
    }

    public string SocketPath => configuration.SocketPath!;

    public string RecordingsDirectory => configuration.OutputRoots![0].Path!;

    public DriverConfiguration Configuration => configuration;

    public string LedgerPath => ledger;

    public static async Task<SyntheticDriverHost> StartAsync(Action<IServiceCollection>? reshape = null)
    {
        string?[] inherited = [.. SettingsThatWouldBindAPort.Select(Environment.GetEnvironmentVariable)];

        foreach (string name in SettingsThatWouldBindAPort)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        string root = Directory.CreateTempSubdirectory("carina-app-swap-").FullName;
        string recordings = Path.Combine(root, "recordings");

        Directory.CreateDirectory(recordings);

        var configuration = new DriverConfiguration(
            Path.Combine(root, "driver.sock"),
            [new OutputRootSettings(RootName, recordings)],
            6,
            new TunerSettings(TunerBackend.Fake),
            [new DeviceSettings("synthetic-terrestrial", DeviceKind.Terrestrial)],
            SocketGroupId: (int)UnixFile.CurrentGroupId());
        string ledger = Path.Combine(root, "driver.json");

        await File.WriteAllTextAsync(ledger, DriverConfigurationWriter.Serialize(configuration));

        IHost host = await RaisedAsync(configuration, ledger, reshape);

        return new SyntheticDriverHost(host, root, ledger, configuration, inherited, reshape);
    }

    public string Beside(string name) => Path.Combine(root, name);

    public void WriteLedger(DriverConfiguration written)
        => File.WriteAllText(ledger, DriverConfigurationWriter.Serialize(written));

    /// <summary>
    /// Puts the driver down and raises another one on the same socket and the same output root. The
    /// new process greets with an instance of its own and holds none of the sessions the one before
    /// it did, which is the whole of what a recording left running has to be recovered from.
    /// </summary>
    public async Task RaiseAnotherDriverAsync()
    {
        await PutDownAsync();

        host = await RaisedAsync(configuration, ledger, reshape);
    }

    /// <summary>
    /// Asks the driver to stop the way its host is asked when the process receives SIGTERM, and hands
    /// back the stop while it is still under way. Delivering the signal to a separate process is not
    /// part of it: the driver here shares the test process.
    /// </summary>
    public Task BeginStop()
    {
        IHost serving = host ?? throw new InvalidOperationException("No driver is running.");

        stopping ??= serving.StopAsync(CancellationToken.None);

        return stopping;
    }

    public async Task PutDownAsync()
    {
        if (host is not { } going)
        {
            return;
        }

        try
        {
            await (stopping ?? going.StopAsync(TimeSpan.FromSeconds(20)));
        }
        finally
        {
            going.Dispose();
            host = null;
            stopping = null;
        }
    }

    /// <summary>
    /// Puts the driver down and starts it again from what the ledger on disk says, the way the
    /// entry point does: the file is read, the filesystem it names is checked, and a finding stops
    /// the start with the exit code and the report the process would give. The shape rules that
    /// want the socket under /run are the one step left out, because a test cannot bind there.
    /// </summary>
    public async Task<int> RaiseFromTheLedgerAsync(TextWriter error)
    {
        await PutDownAsync();

        DriverConfiguration? written = DriverConfigurationReader.Parse(await File.ReadAllTextAsync(ledger));

        Assert.NotNull(written);

        int exitCode = DriverStartup.Report(DriverConfigurationReader.CheckTheFilesystem(written), error, ledger);

        if (exitCode is not 0)
        {
            return exitCode;
        }

        configuration = written;
        host = await RaisedAsync(written, ledger, reshape);

        return exitCode;
    }

    public HeldSession Held(SessionId sessionId)
    {
        IHost serving = host ?? throw new InvalidOperationException("No driver is running.");
        TunerSessionManager manager = serving.Services.GetRequiredService<TunerSessionManager>();

        Assert.True(
            manager.TryGet(sessionId, out TunerSession? session),
            $"The driver holds no session named {sessionId.Value}.");

        return new HeldSession(session.State, session.StopReason, session.BytesRecorded);
    }

    private static async Task<IHost> RaisedAsync(
        DriverConfiguration configuration,
        string ledger,
        Action<IServiceCollection>? reshape)
    {
        DriverHostResult built = DriverHost.Create(
            [],
            configuration,
            services =>
            {
                services.AddSingleton<ITunerDeviceFactory>(new PacedTuners(BetweenReads));
                reshape?.Invoke(services);
            },
            ledger);

        Assert.True(built.TryGetHost(out IHost? host), string.Join(" ", built.Problems));

        await host.StartAsync();

        return host;
    }

    public static SessionCounters ContinuityOf(string path)
    {
        var reader = new TsPacketReader();
        var tracker = new ContinuityCounterTracker();
        byte[] buffer = new byte[64 * 1024];

        using FileStream file = File.OpenRead(path);

        int read = file.Read(buffer, 0, buffer.Length);

        while (read > 0)
        {
            foreach (TsPacket packet in reader.Read(buffer.AsSpan(0, read)))
            {
                tracker.Observe(packet);
            }

            read = file.Read(buffer, 0, buffer.Length);
        }

        return tracker.Snapshot();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await PutDownAsync();
        }
        finally
        {
            for (int index = 0; index < SettingsThatWouldBindAPort.Length; index++)
            {
                Environment.SetEnvironmentVariable(
                    SettingsThatWouldBindAPort[index],
                    inherited[index]);
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
