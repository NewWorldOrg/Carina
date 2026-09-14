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
    private readonly DriverConfiguration configuration;
    private readonly IReadOnlyList<string?> inherited;

    private IHost host;

    private SyntheticDriverHost(
        IHost host,
        string root,
        string ledger,
        DriverConfiguration configuration,
        IReadOnlyList<string?> inherited)
    {
        this.host = host;
        this.root = root;
        this.ledger = ledger;
        this.configuration = configuration;
        this.inherited = inherited;
        SocketPath = configuration.SocketPath!;
        RecordingsDirectory = configuration.OutputRoots![0].Path!;
    }

    public string SocketPath { get; }

    public string RecordingsDirectory { get; }

    public static async Task<SyntheticDriverHost> StartAsync()
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

        IHost host = await RaisedAsync(configuration, ledger);

        return new SyntheticDriverHost(host, root, ledger, configuration, inherited);
    }

    /// <summary>
    /// Puts the driver down and raises another one on the same socket and the same output root. The
    /// new process greets with an instance of its own and holds none of the sessions the one before
    /// it did, which is the whole of what a recording left running has to be recovered from.
    /// </summary>
    public async Task RaiseAnotherDriverAsync()
    {
        await host.StopAsync(TimeSpan.FromSeconds(20));

        host.Dispose();
        host = await RaisedAsync(configuration, ledger);
    }

    private static async Task<IHost> RaisedAsync(DriverConfiguration configuration, string ledger)
    {
        DriverHostResult built = DriverHost.Create(
            [],
            configuration,
            services => services.AddSingleton<ITunerDeviceFactory>(new PacedTuners(BetweenReads)),
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
            await host.StopAsync(TimeSpan.FromSeconds(20));
        }
        finally
        {
            host.Dispose();

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
