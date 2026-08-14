using Carina.Driver.Configuration;
using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tests;

public sealed class DvbDeviceProbeTests : IDisposable
{
    private readonly string work = Directory.CreateTempSubdirectory("carina-dvb-").FullName;

    public void Dispose() => Directory.Delete(work, recursive: true);

    [Fact]
    public void EveryFrontendUnderEveryAdapterIsFound()
    {
        Node("adapter0", "frontend0");
        Node("adapter1", "frontend0");
        Node("adapter2", "frontend0");

        Assert.Equal(
            [
                Path.Combine(work, "adapter0", "frontend0"),
                Path.Combine(work, "adapter1", "frontend0"),
                Path.Combine(work, "adapter2", "frontend0"),
            ],
            DvbDeviceProbe.FrontendPathsUnder(work)
        );
    }

    [Fact]
    public void NodesThatAreNotFrontendsAreLeftAlone()
    {
        Node("adapter0", "frontend0");
        Node("adapter0", "demux0");
        Node("adapter0", "dvr0");

        Assert.Equal(
            [Path.Combine(work, "adapter0", "frontend0")],
            DvbDeviceProbe.FrontendPathsUnder(work)
        );
    }

    [Fact]
    public void AMachineWithNoTunerTreeFindsNothingRatherThanThrowing()
    {
        Assert.Empty(DvbDeviceProbe.FrontendPathsUnder(Path.Combine(work, "absent")));
    }

    [Fact]
    public void ATunerThatEnumeratesOnlyTerrestrialIsTerrestrial()
    {
        Assert.Equal(
            DeviceKind.Terrestrial,
            DvbDeviceProbe.KindOf([DeliverySystem.IsdbTerrestrial], string.Empty)
        );
    }

    [Fact]
    public void ATunerThatEnumeratesOnlySatelliteIsSatellite()
    {
        Assert.Equal(
            DeviceKind.Satellite,
            DvbDeviceProbe.KindOf([DeliverySystem.IsdbSatellite], string.Empty)
        );
    }

    [Theory]
    [InlineData("PT3 ISDB-S", DeviceKind.Satellite)]
    [InlineData("PT3 ISDB-T", DeviceKind.Terrestrial)]
    [InlineData("isdbt demodulator", DeviceKind.Terrestrial)]
    public void ATunerThatWillNotEnumerateFallsBackToWhatItCallsItself(
        string name,
        DeviceKind kind
    )
    {
        Assert.Equal(kind, DvbDeviceProbe.KindOf([], name));
    }

    [Fact]
    public void ATunerThatEnumeratesBothFallsBackToWhatItCallsItself()
    {
        Assert.Equal(
            DeviceKind.Satellite,
            DvbDeviceProbe.KindOf(
                [DeliverySystem.IsdbTerrestrial, DeliverySystem.IsdbSatellite],
                "PT3 ISDB-S"
            )
        );
    }

    [Fact]
    public void ATunerThatSaysNothingUsefulIsLeftUnspecifiedRatherThanGuessed()
    {
        Assert.Equal(DeviceKind.Unspecified, DvbDeviceProbe.KindOf([], "some other card"));
        Assert.Equal(DeviceKind.Unspecified, DvbDeviceProbe.KindOf([new DeliverySystem(3)], ""));
    }

    [Fact]
    public void InspectingReportsWhatEachFrontendSaidAboutItself()
    {
        var calls = new ScriptedDvbSystemCalls
        {
            DeliverySystems = [DeliverySystem.IsdbSatellite],
            HardwareName = "PT3 ISDB-S",
        };
        var probe = new DvbDeviceProbe(calls);

        var detected = Assert.Single(probe.Inspect(["/dev/dvb/adapter0/frontend0"]));

        Assert.Equal("/dev/dvb/adapter0/frontend0", detected.FrontendPath);
        Assert.Equal("PT3 ISDB-S", detected.Name);
        Assert.Equal(DeviceKind.Satellite, detected.Kind);
        Assert.Null(detected.Problem);
    }

    [Fact]
    public void AFrontendAnotherProcessHoldsIsReportedWithItsReasonRatherThanDropped()
    {
        var calls = new ScriptedDvbSystemCalls();
        calls.RefuseToOpen("/dev/dvb/adapter0/frontend0", Errno.Busy);
        var probe = new DvbDeviceProbe(calls);

        var detected = Assert.Single(probe.Inspect(["/dev/dvb/adapter0/frontend0"]));

        Assert.Equal(DeviceKind.Unspecified, detected.Kind);
        Assert.NotNull(detected.Problem);
        Assert.Contains("already holding this tuner", detected.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void InspectingOpensEachFrontendReadOnlyAndGivesItBack()
    {
        var calls = new ScriptedDvbSystemCalls
        {
            DeliverySystems = [DeliverySystem.IsdbTerrestrial],
            HardwareName = "PT3 ISDB-T",
        };
        var probe = new DvbDeviceProbe(calls);

        probe.Inspect(["/dev/dvb/adapter0/frontend0"]);

        Assert.Equal(DvbAccess.Inspect, Assert.Single(calls.Opened).Access);
        Assert.Single(calls.Closed);
    }

    [Fact]
    public void AFrontendThatSaysNothingAtAllIsReportedAsUnavailableRatherThanAsATuner()
    {
        var calls = new ScriptedDvbSystemCalls
        {
            DeliverySystems = [],
            RefuseInfoWith = Errno.NoSuchDevice,
        };
        var probe = new DvbDeviceProbe(calls);

        var detected = Assert.Single(probe.Inspect(["/dev/dvb/adapter0/frontend0"]));

        Assert.Equal(DeviceKind.Unspecified, detected.Kind);
        Assert.NotNull(detected.Problem);
    }

    private void Node(string adapter, string node)
    {
        Directory.CreateDirectory(Path.Combine(work, adapter));
        File.WriteAllText(Path.Combine(work, adapter, node), string.Empty);
    }
}
