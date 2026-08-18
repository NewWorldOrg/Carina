using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Tuning;
using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tests;

public sealed class TunerDetectorTests : IDisposable
{
    private const string TerrestrialName = "synthetic ISDB-T demodulator";
    private const string SatelliteName = "synthetic ISDB-S demodulator";

    private readonly string root = Directory.CreateTempSubdirectory("carina-detect-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void EveryFrontendUnderEveryAdapterIsDetected()
    {
        Node("adapter0", "frontend0");
        Node("adapter1", "frontend0");

        Assert.Equal(
            ["adapter0.frontend0", "adapter1.frontend0"],
            Detect(Terrestrial()).Select(detection => detection.DeviceId)
        );
    }

    [Fact]
    public void ATunerIsNamedByItsAdapterAndNodeRatherThanByItsDeviceNode()
    {
        Node("adapter2", "frontend1");

        TunerDetection detection = Assert.Single(Detect(Terrestrial()));

        Assert.Equal("adapter2.frontend1", detection.DeviceId);
        Assert.DoesNotContain('/', detection.DeviceId);
    }

    [Fact]
    public void AMachineWithNoTunerTreeDetectsNothingRatherThanThrowing()
    {
        Assert.Empty(
            DvbTunerDetector.Using(Terrestrial(), Path.Combine(root, "absent")).Detect()
        );
    }

    [Fact]
    public void ATunerThatEnumeratesItsDeliverySystemsIsDetectedFromThatEnumeration()
    {
        Node("adapter0", "frontend0");

        TunerDetection detection = Assert.Single(Detect(Terrestrial()));

        Assert.Equal(DeviceDetection.Detected, detection.Detection);
        Assert.Equal([DeviceKind.Terrestrial], detection.Receives);
    }

    [Fact]
    public void ATunerThatEnumeratesBothDeliverySystemsIsDetectedAsReceivingBoth()
    {
        Node("adapter0", "frontend0");

        var calls = new ScriptedDvbSystemCalls
        {
            DeliverySystems = [DeliverySystem.IsdbTerrestrial, DeliverySystem.IsdbSatellite],
            HardwareName = SatelliteName,
        };

        Assert.Equal(
            [DeviceKind.Terrestrial, DeviceKind.Satellite],
            Assert.Single(Detect(calls)).Receives
        );
    }

    [Fact]
    public void ATunerThatWillNotEnumerateIsDetectedFromWhatItCallsItself()
    {
        Node("adapter0", "frontend0");

        var calls = new ScriptedDvbSystemCalls
        {
            RefuseProperty = DvbProperty.EnumerateDeliverySystems,
            HardwareName = SatelliteName,
        };

        TunerDetection detection = Assert.Single(Detect(calls));

        Assert.Equal(DeviceDetection.Detected, detection.Detection);
        Assert.Equal([DeviceKind.Satellite], detection.Receives);
    }

    [Fact]
    public void AFrontendAnotherProcessHoldsIsStillListedWithItsReason()
    {
        Node("adapter0", "frontend0");

        ScriptedDvbSystemCalls calls = Terrestrial();
        calls.RefuseToOpen(Path.Combine(root, "adapter0", "frontend0"), Errno.Busy);

        TunerDetection detection = Assert.Single(Detect(calls));

        Assert.Equal(DeviceDetection.Busy, detection.Detection);
        Assert.Empty(detection.Receives);
        Assert.NotNull(detection.Detail);
    }

    [Fact]
    public void TheReasonATunerCouldNotBeOpenedNamesTheTunerRatherThanItsDeviceNode()
    {
        Node("adapter0", "frontend0");

        ScriptedDvbSystemCalls calls = Terrestrial();
        calls.RefuseToOpen(Path.Combine(root, "adapter0", "frontend0"), Errno.PermissionDenied);

        TunerDetection detection = Assert.Single(Detect(calls));

        Assert.NotNull(detection.Detail);
        Assert.DoesNotContain(root, detection.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain('/', detection.Detail);
        Assert.Contains("adapter0.frontend0", detection.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSyntheticBackendDetectsTheTunersTheConfigurationDeclares()
    {
        IReadOnlyList<TunerDetection> detected = new FakeTunerDetector(Configured()).Detect();

        Assert.Equal(
            ["fake-terrestrial", "fake-satellite"],
            detected.Select(detection => detection.DeviceId)
        );
        Assert.All(detected, detection =>
            Assert.Equal(DeviceDetection.Detected, detection.Detection)
        );
    }

    [Fact]
    public void ASyntheticTunerReceivesWhatTheConfigurationCallsIt()
    {
        IReadOnlyList<TunerDetection> detected = new FakeTunerDetector(Configured()).Detect();

        Assert.Equal([DeviceKind.Terrestrial], detected[0].Receives);
        Assert.Equal([DeviceKind.Satellite], detected[1].Receives);
    }

    [Fact]
    public void ASyntheticTunerWithoutAKindIsNotDetectedAsAWorkingTuner()
    {
        DriverConfiguration configuration = Configured() with
        {
            Devices = [new DeviceSettings("fake-nameless", DeviceKind.Unspecified)],
        };

        TunerDetection detection = Assert.Single(new FakeTunerDetector(configuration).Detect());

        Assert.Equal(DeviceDetection.Unreadable, detection.Detection);
        Assert.Empty(detection.Receives);
    }

    [Fact]
    public void TheBackendDecidesWhichDetectorTheDriverUses()
    {
        Assert.IsType<FakeTunerDetector>(TunerDetectors.For(Configured()));
        Assert.IsType<DvbTunerDetector>(
            TunerDetectors.For(Configured() with { Tuner = new TunerSettings(TunerBackend.Dvb) })
        );
    }

    [Fact]
    public void ADriverWithoutAnEstablishedBackendDetectsNothingRatherThanGuessing()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TunerDetectors.For(Configured() with { Tuner = null })
        );
    }

    private static DriverConfiguration Configured() =>
        new(
            "/run/carina/driver.sock",
            [new OutputRootSettings("primary", "/srv/recordings")],
            6,
            new TunerSettings(TunerBackend.Fake),
            [
                new DeviceSettings("fake-terrestrial", DeviceKind.Terrestrial),
                new DeviceSettings("fake-satellite", DeviceKind.Satellite),
            ]
        );

    private static ScriptedDvbSystemCalls Terrestrial() =>
        new()
        {
            DeliverySystems = [DeliverySystem.IsdbTerrestrial],
            HardwareName = TerrestrialName,
        };

    private IReadOnlyList<TunerDetection> Detect(ScriptedDvbSystemCalls calls) =>
        DvbTunerDetector.Using(calls, root).Detect();

    private void Node(string adapter, string node)
    {
        Directory.CreateDirectory(Path.Combine(root, adapter));
        File.WriteAllText(Path.Combine(root, adapter, node), string.Empty);
    }
}
