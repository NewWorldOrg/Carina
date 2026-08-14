using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Tuning;
using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tests;

public sealed class TunerDeviceFactoryDvbTests
{
    private const int SyntheticStream = 50_001;


    private const FrontendStatus Locked =
        FrontendStatus.Signal
        | FrontendStatus.Carrier
        | FrontendStatus.Viterbi
        | FrontendStatus.Sync
        | FrontendStatus.Lock;

    [Fact]
    public void TheSyntheticBackendStillAnswersWithoutTouchingAnyDevice()
    {
        var calls = new ScriptedDvbSystemCalls();
        var factory = TunerDeviceFactory.Using(
            Configured(TunerBackend.Fake),
            TimeProvider.System,
            calls
        );

        using var device = factory.Create(
            new DeviceSettings("fake-terrestrial", DeviceKind.Terrestrial),
            new TuningRequest(TunerKind.Terrestrial, 27),
            tune: null
        );

        Assert.IsType<FakeTunerDevice>(device);
        Assert.Empty(calls.Opened);
    }

    [Fact]
    public void TheDvbBackendOpensTheConfiguredFrontend()
    {
        var (calls, clock) = Ready();
        var factory = TunerDeviceFactory.Using(Configured(TunerBackend.Dvb), clock, calls);

        using var device = factory.Create(
            Terrestrial(),
            new TuningRequest(TunerKind.Terrestrial, 27),
            tune: null
        );

        Assert.IsType<DvbTunerDevice>(device);
        Assert.Equal("/dev/dvb/adapter0/frontend0", calls.Opened[0].Path);
    }

    [Fact]
    public void ADeviceWithNoUsablePathIsRefusedByNameBeforeAnythingIsOpened()
    {
        var (calls, clock) = Ready();
        var factory = TunerDeviceFactory.Using(Configured(TunerBackend.Dvb), clock, calls);

        var refusal = Assert.Throws<DvbDeviceException>(
            () =>
                factory.Create(
                    new DeviceSettings("pt3-0", DeviceKind.Terrestrial, "/dev/video0"),
                    new TuningRequest(TunerKind.Terrestrial, 27),
                    tune: null
                )
        );

        Assert.Contains("pt3-0", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("/dev/dvb/", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(calls.Opened);
    }

    [Fact]
    public void AMissingDeviceNodeIsRefusedWithTheSamePlainnessAsABadSetting()
    {
        var (calls, clock) = Ready();
        calls.RefuseToOpen("/dev/dvb/adapter0/frontend0", Errno.NoSuchDevice);
        var factory = TunerDeviceFactory.Using(Configured(TunerBackend.Dvb), clock, calls);

        var refusal = Assert.Throws<DvbDeviceException>(
            () => factory.Create(Terrestrial(), new TuningRequest(TunerKind.Terrestrial, 27), tune: null)
        );

        Assert.Contains("/dev/dvb/adapter0/frontend0", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("errno 19", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASatelliteDeviceWithTheAerialSupplyOffIsToldSoExplicitly()
    {
        var (calls, clock) = Ready();
        var factory = TunerDeviceFactory.Using(Configured(TunerBackend.Dvb), clock, calls);

        var tune = TuneParams.Bs(1, SyntheticStream);

        using var device = factory.Create(
            new DeviceSettings("pt3-2", DeviceKind.Satellite, "/dev/dvb/adapter2/frontend0"),
            tune.ToLegacyRequest(),
            tune
        );

        Assert.Equal(LnbVoltage.Off, Assert.Single(calls.VoltagesSet));
    }

    [Fact]
    public void ASatelliteDeviceWithTheAerialSupplyEnabledFeedsTheAerial()
    {
        var (calls, clock) = Ready();
        var factory = TunerDeviceFactory.Using(Configured(TunerBackend.Dvb), clock, calls);

        var tune = TuneParams.Bs(1, SyntheticStream);

        using var device = factory.Create(
            new DeviceSettings(
                "pt3-2",
                DeviceKind.Satellite,
                "/dev/dvb/adapter2/frontend0",
                LnbPower: true
            ),
            tune.ToLegacyRequest(),
            tune
        );

        Assert.Equal(LnbVoltage.Eighteen, Assert.Single(calls.VoltagesSet));
    }

    [Fact]
    public void AnEvenSatelliteSlotInTheOlderParametersIsReadAsCommunication()
    {
        Assert.IsType<CommunicationSatelliteChannel>(
            DvbTuneRequest.Resolve(null, new TuningRequest(TunerKind.Satellite, 24))
        );
    }

    [Fact]
    public void AnOddSatelliteSlotInTheOlderParametersIsRefusedForNamingNoTransportStream()
    {
        var refusal = Assert.Throws<DvbDeviceException>(
            () => DvbTuneRequest.Resolve(null, new TuningRequest(TunerKind.Satellite, 15))
        );

        Assert.Contains("transport stream", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("15", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AServiceIdInTheOlderParametersIsNotMistakenForATransportStreamId()
    {
        var refusal = Assert.Throws<DvbDeviceException>(
            () => DvbTuneRequest.Resolve(null, new TuningRequest(TunerKind.Satellite, 15, ServiceId: 1024))
        );

        Assert.DoesNotContain("1024", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARequestThatDoesNotSayWhichAerialItNeedsIsRefused()
    {
        var refusal = Assert.Throws<DvbDeviceException>(
            () => DvbTuneRequest.Resolve(null, new TuningRequest(TunerKind.Unspecified, 27))
        );

        Assert.Contains("terrestrial or satellite", refusal.Message, StringComparison.Ordinal);
    }

    private static DriverConfiguration Configured(TunerBackend backend) =>
        new(null, null, 0, new TunerSettings(backend), null);

    private static DeviceSettings Terrestrial() =>
        new("pt3-0", DeviceKind.Terrestrial, "/dev/dvb/adapter0/frontend0");

    private static (ScriptedDvbSystemCalls Calls, ManualTimeProvider Clock) Ready()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var calls = new ScriptedDvbSystemCalls(clock);
        calls.ReportStatus(Locked);

        return (calls, clock);
    }
}
