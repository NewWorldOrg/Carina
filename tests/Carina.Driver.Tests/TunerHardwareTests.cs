using System.Net;

using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Ipc;
using Carina.Driver.Tuning;
using Carina.Driver.Tuning.Dvb;
using Carina.Driver.Tuning.Usb;
using Carina.Driver.Tuning.Usb.CechZd1;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Driver.Tests;

public sealed class DvbTunerHardwareTests : IDisposable
{
    private const string FrontendName = "Example ISDB-T frontend";

    private readonly string root = Directory.CreateTempSubdirectory("carina-hw-dev-").FullName;

    private readonly string sysfs = Directory.CreateTempSubdirectory("carina-hw-sys-").FullName;

    public void Dispose()
    {
        Directory.Delete(root, recursive: true);
        Directory.Delete(sysfs, recursive: true);
    }

    [Fact]
    public void ADetectedFrontendCarriesTheNameItGivesItself()
    {
        Node("adapter0", "frontend0");

        DeviceHardware? hardware = Assert.Single(Detect(Named())).Hardware;

        Assert.Equal(FrontendName, hardware?.Name);
        Assert.Equal(Path.Combine(root, "adapter0", "frontend0"), Assert.Single(Detect(Named())).DevicePath);
    }

    [Fact]
    public void TheKernelDriverAndTheBusAreReadFromWhereTheKernelFiledTheFrontend()
    {
        Node("adapter1", "frontend0");
        Filed("dvb1.frontend0", driver: "example_dvb", subsystem: "pci");

        DeviceHardware? hardware = Assert.Single(Detect(Named())).Hardware;

        Assert.Equal("example_dvb", hardware?.KernelDriver);
        Assert.Equal(TunerBus.Pci, hardware?.Bus);
    }

    [Fact]
    public void AFrontendOnAUsbStickSaysSo()
    {
        Node("adapter0", "frontend0");
        Filed("dvb0.frontend0", driver: "example_usb_dvb", subsystem: "usb");

        Assert.Equal(TunerBus.Usb, Assert.Single(Detect(Named())).Hardware?.Bus);
    }

    [Fact]
    public void AFrontendTheKernelHasNotFiledIsNamedWithoutADriver()
    {
        Node("adapter0", "frontend0");

        DeviceHardware? hardware = Assert.Single(Detect(Named())).Hardware;

        Assert.Null(hardware?.KernelDriver);
        Assert.Equal(TunerBus.Unspecified, hardware?.Bus);
    }

    [Fact]
    public void AFrontendAnotherProcessHoldsStillSaysWhichDriverItBelongsTo()
    {
        Node("adapter0", "frontend0");
        Filed("dvb0.frontend0", driver: "example_dvb", subsystem: "pci");

        ScriptedDvbSystemCalls calls = Named();
        calls.RefuseToOpen(Path.Combine(root, "adapter0", "frontend0"), Errno.Busy);

        DeviceHardware? hardware = Assert.Single(Detect(calls)).Hardware;

        Assert.Null(hardware?.Name);
        Assert.Equal("example_dvb", hardware?.KernelDriver);
    }

    private ScriptedDvbSystemCalls Named() =>
        new() { DeliverySystems = [DeliverySystem.IsdbTerrestrial], HardwareName = FrontendName };

    private IReadOnlyList<TunerDetection> Detect(ScriptedDvbSystemCalls calls) =>
        DvbTunerDetector.Using(calls, root, sysfs).Detect();

    private void Node(string adapter, string node)
    {
        Directory.CreateDirectory(Path.Combine(root, adapter));
        File.WriteAllText(Path.Combine(root, adapter, node), string.Empty);
    }

    private void Filed(string entry, string driver, string subsystem)
    {
        string device = Path.Combine(sysfs, "devices", entry);
        Directory.CreateDirectory(Path.Combine(sysfs, "drivers", driver));
        Directory.CreateDirectory(Path.Combine(sysfs, "bus", subsystem));
        Directory.CreateDirectory(device);
        Directory.CreateSymbolicLink(Path.Combine(device, "driver"), Path.Combine("..", "..", "drivers", driver));
        Directory.CreateSymbolicLink(Path.Combine(device, "subsystem"), Path.Combine("..", "..", "bus", subsystem));
        Directory.CreateDirectory(Path.Combine(sysfs, entry));
        Directory.CreateSymbolicLink(Path.Combine(sysfs, entry, "device"), Path.Combine("..", "devices", entry));
    }
}

public sealed class UsbAndSyntheticTunerHardwareTests
{
    [Fact]
    public void AUsbTunerIsNamedForItsModelAndSaysWhichPortItHangsOn()
    {
        var host = new FakeUsbHost(new FakeBridge());
        host.Nodes.Add(new UsbDeviceNode("/dev/bus/usb/001/002", CechZd1TunerDevice.Model, "1-1"));

        DeviceHardware? hardware = Assert.Single(UsbTunerDetector.Using(host).Detect()).Hardware;

        Assert.Equal(CechZd1TunerDevice.ProductName, hardware?.Name);
        Assert.Equal(TunerBus.Usb, hardware?.Bus);
        Assert.Equal("1-1", hardware?.UsbPort);
        Assert.Null(hardware?.KernelDriver);
    }

    [Fact]
    public void ASyntheticTunerSaysItIsSyntheticAndHasNoName()
    {
        var configuration = new DriverConfiguration(
            "/run/carina/driver.sock",
            [new OutputRootSettings("primary", "/srv/recordings")],
            6,
            new TunerSettings(TunerBackend.Fake),
            [new DeviceSettings("fake-terrestrial", DeviceKind.Terrestrial)]
        );

        DeviceHardware? hardware = Assert.Single(new FakeTunerDetector(configuration).Detect()).Hardware;

        Assert.Equal(TunerBus.Synthetic, hardware?.Bus);
        Assert.Null(hardware?.Name);
    }
}

public sealed class TunerHardwareBookTests
{
    private static readonly DriverConfiguration Dvb = new(
        "/run/carina/driver.sock",
        [new OutputRootSettings("primary", "/srv/recordings")],
        6,
        new TunerSettings(TunerBackend.Dvb),
        [new DeviceSettings("adapter0.frontend0", DeviceKind.Terrestrial, DevicePath: "/dev/dvb/adapter0/frontend0")]
    );

    [Fact]
    public void ANameReadOnceIsKeptWhenTheFrontendIsLaterHeldByAnotherProcess()
    {
        var book = new TunerHardwareBook(Dvb, TimeProvider.System);

        book.Remember([Found("Example ISDB-T frontend", DeviceDetection.Detected)]);
        book.Remember([Found(null, DeviceDetection.Busy)]);

        Assert.Equal("Example ISDB-T frontend", book.For(Dvb.Devices![0]).Name);
    }

    [Fact]
    public void ATunerNeverDetectedIsDescribedFromTheConfiguration()
    {
        TunerHardwareDto hardware = new TunerHardwareBook(Dvb, TimeProvider.System).For(Dvb.Devices![0]);

        Assert.Null(hardware.Name);
        Assert.Equal("/dev/dvb/adapter0/frontend0", hardware.DevicePath);
    }

    private static TunerDetection Found(string? name, DeviceDetection detection) =>
        new(
            "adapter0.frontend0",
            detection is DeviceDetection.Detected ? [DeviceKind.Terrestrial] : [],
            detection,
            null,
            "/dev/dvb/adapter0/frontend0",
            new DeviceHardware(name, "example_dvb", TunerBus.Pci, null)
        );
}

public sealed class TunerHardwareApiTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static CancellationToken Soon() => new CancellationTokenSource(Patience).Token;

    [Fact]
    public async Task ATunerAnswersWithTheHardwareFoundAtStartup()
    {
        var detector = new ScriptedTunerDetector(
            new TunerDetection(
                "fake-terrestrial",
                [DeviceKind.Terrestrial],
                DeviceDetection.Detected,
                null,
                "/dev/dvb/adapter0/frontend0",
                new DeviceHardware("Example ISDB-T frontend", "example_dvb", TunerBus.Pci, null)
            )
        );

        await using DriverUnderTest driver = await DriverUnderTest.Start(
            reshapeServices: services => services.AddSingleton<ITunerDetector>(detector)
        );
        using HttpClient client = driver.Client();

        TunerSnapshot tuner = Assert.Single(await Tuners(client), tuner => tuner.DeviceId is "fake-terrestrial");

        Assert.Equal("Example ISDB-T frontend", tuner.Hardware?.Name);
        Assert.Equal("example_dvb", tuner.Hardware?.KernelDriver);
        Assert.Equal(TunerBus.Pci, tuner.Hardware?.Bus);
        Assert.Equal("/dev/dvb/adapter0/frontend0", tuner.Hardware?.DevicePath);
        Assert.Null(tuner.Machine);
    }

    [Fact]
    public async Task ATunerOfThisMachineHasNoMachineNamed()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        Assert.All(await Tuners(client), tuner =>
        {
            Assert.Null(tuner.Machine);
            Assert.Equal(TunerBus.Synthetic, tuner.Hardware?.Bus);
        });
    }

    [Fact]
    public async Task ADetectedDeviceAnswersWithItsHardware()
    {
        var detector = new ScriptedTunerDetector(
            new TunerDetection(
                "usb1-1",
                [DeviceKind.Terrestrial],
                DeviceDetection.Detected,
                null,
                "/dev/bus/usb/001/002",
                new DeviceHardware(CechZd1TunerDevice.ProductName, null, TunerBus.Usb, "1-1")
            )
        );

        await using DriverUnderTest driver = await DriverUnderTest.Start(
            reshapeServices: services => services.AddSingleton<ITunerDetector>(detector)
        );
        using HttpClient client = driver.Client();

        using HttpResponseMessage response = await client.GetAsync(DriverEndpoints.DevicesDetected, Soon());
        IReadOnlyList<DetectedDeviceDto>? detected = await DriverUnderTest.Read(
            response,
            DriverJson.Context.IReadOnlyListDetectedDeviceDto
        );

        DetectedDeviceDto device = Assert.Single(detected ?? []);
        Assert.Equal(CechZd1TunerDevice.ProductName, device.Hardware?.Name);
        Assert.Equal("1-1", device.Hardware?.UsbPort);
        Assert.Equal("/dev/bus/usb/001/002", device.Hardware?.DevicePath);
    }

    [Fact]
    public void TheDriverSaysItNamesItsHardware()
    {
        Assert.Contains(DriverCapabilities.DeviceHardware, DriverGreeting.Capabilities);
    }

    private static async Task<IReadOnlyList<TunerSnapshot>> Tuners(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync(DriverEndpoints.Tuners, Soon());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IReadOnlyList<TunerSnapshot>? tuners = await DriverUnderTest.Read(
            response,
            DriverJson.Context.IReadOnlyListTunerSnapshot
        );

        Assert.NotNull(tuners);

        return tuners;
    }
}
