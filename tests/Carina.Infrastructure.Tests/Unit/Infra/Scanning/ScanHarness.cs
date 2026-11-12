using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Scans;
using Carina.Infrastructure.Driver;
using Carina.Infrastructure.Scanning;
using Carina.Infrastructure.Tests.Fixtures.Clocks;
using Carina.Infrastructure.Tests.Reservations;
using Carina.TestSupport;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Unit.Infra.Scanning;

public sealed class ScanHarness
{
    public ScanHarness(
        ScriptedDriverClient driver,
        TimeProvider? clock = null,
        ScanSettings? settings = null,
        TunerCapacity? capacity = null)
    {
        Driver = driver;
        Clock = clock ?? new StillClock();
        Settings = settings ?? ScanSettings.Default;
        Capacity = new HeldSeating(capacity);

        Orchestrator = new ChannelScanOrchestrator(
            Driver,
            new ScanPlanningDomainService(Capacity, SatelliteStreams),
            Signals,
            Runs,
            new ChannelRotationDomainService(Services, Candidates),
            new ChannelLineupDomainService(Services, Candidates),
            Events,
            Clock,
            Settings);
    }

    public ScriptedDriverClient Driver { get; }

    public TimeProvider Clock { get; }

    public ScanSettings Settings { get; }

    internal HeldSeating Capacity { get; }

    public DriverSignalRelay Signals { get; } = new(NullLogger<DriverSignalRelay>.Instance);

    public HeldScanRuns Runs { get; } = new();

    public HeldServices Services { get; } = new();

    public HeldCandidates Candidates { get; } = new();

    public HeldSatelliteStreams SatelliteStreams { get; } = new();

    public RecordingAppEvents Events { get; } = new();

    public IChannelScanOrchestrator Orchestrator { get; }

    public void RestartTheDriver()
    {
        Driver.InstanceId = "instance-b";
        Signals.Publish(DriverClientSignals.InstanceChanged);
    }
}
