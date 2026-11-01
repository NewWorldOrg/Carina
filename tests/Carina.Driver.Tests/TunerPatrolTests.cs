using System.Net;

using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Tuning;
using Carina.Driver.Tuning.Usb.CechZd1;

namespace Carina.Driver.Tests;

public sealed class TunerPresenceTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("carina-presence-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void AFrontendIsPresentWhileItsNodeIsThere()
    {
        string frontend = Path.Combine(root, "frontend0");
        File.WriteAllText(frontend, string.Empty);
        var device = new DeviceSettings("adapter0.frontend0", DeviceKind.Terrestrial, frontend);

        Assert.True(new DvbPresence().IsPresent(device, new TunerHardwareDto { DevicePath = frontend }));

        File.Delete(frontend);

        Assert.False(new DvbPresence().IsPresent(device, new TunerHardwareDto { DevicePath = frontend }));
    }

    [Fact]
    public void AUsbTunerIsPresentWhileItsPortHoldsTheSameModel()
    {
        var presence = new UsbPresence(root);
        var device = new DeviceSettings("usb1-2", DeviceKind.Terrestrial);

        Assert.False(presence.IsPresent(device, new TunerHardwareDto { UsbPort = "1-2" }));

        Port("1-2", CechZd1TunerDevice.Model.VendorId, CechZd1TunerDevice.Model.ProductId);

        Assert.True(presence.IsPresent(device, new TunerHardwareDto { UsbPort = "1-2" }));
    }

    [Fact]
    public void APortHoldingAnotherDeviceDoesNotCountAsTheTuner()
    {
        Port("1-2", 0x1234, 0x5678);

        Assert.False(
            new UsbPresence(root).IsPresent(
                new DeviceSettings("usb1-2", DeviceKind.Terrestrial),
                new TunerHardwareDto { UsbPort = "1-2" }
            )
        );
    }

    [Fact]
    public void AUsbTunerNeverDetectedIsLookedForAtThePortItsIdNames()
    {
        Port("1-2", CechZd1TunerDevice.Model.VendorId, CechZd1TunerDevice.Model.ProductId);

        Assert.True(
            new UsbPresence(root).IsPresent(new DeviceSettings("usb1-2", DeviceKind.Terrestrial), new TunerHardwareDto())
        );
    }

    private void Port(string port, ushort vendor, ushort product)
    {
        string directory = Path.Combine(root, port);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "idVendor"), $"{vendor:x4}\n");
        File.WriteAllText(Path.Combine(directory, "idProduct"), $"{product:x4}\n");
    }
}

public sealed class TunerPatrolTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    private static readonly DriverConfiguration Configuration = new(
        "/run/carina/driver.sock",
        [new OutputRootSettings("primary", "/srv/recordings")],
        6,
        new TunerSettings(TunerBackend.Dvb),
        [
            new DeviceSettings("adapter0.frontend0", DeviceKind.Terrestrial, "/dev/dvb/adapter0/frontend0"),
            new DeviceSettings("adapter1.frontend0", DeviceKind.Terrestrial, "/dev/dvb/adapter1/frontend0"),
        ]
    );

    [Fact]
    public void AnIdleTunerFoundInItsPlaceIsMarkedSeenNow()
    {
        var clock = new ManualTimeProvider(Start);
        var book = new TunerHardwareBook(Configuration, clock);
        var presence = new ScriptedPresence();

        clock.Advance(TimeSpan.FromMinutes(1));
        Patrol(book, presence, clock, busy: []).Walk();

        TunerHardwareDto hardware = book.For(Configuration.Devices![0]);
        Assert.True(hardware.Present);
        Assert.Equal(Start.AddMinutes(1), hardware.LastSeenAt);
    }

    [Fact]
    public void AnIdleTunerGoneFromItsPlaceIsMarkedMissingAndKeepsWhenItWasLastSeen()
    {
        var clock = new ManualTimeProvider(Start);
        var book = new TunerHardwareBook(Configuration, clock);
        var presence = new ScriptedPresence();
        TunerPatrol patrol = Patrol(book, presence, clock, busy: []);

        patrol.Walk();
        presence.Missing.Add("adapter0.frontend0");
        clock.Advance(TimeSpan.FromMinutes(1));
        patrol.Walk();

        TunerHardwareDto hardware = book.For(Configuration.Devices![0]);
        Assert.False(hardware.Present);
        Assert.Equal(Start, hardware.LastSeenAt);
    }

    [Fact]
    public void ATunerPluggedBackInIsPresentAgain()
    {
        var clock = new ManualTimeProvider(Start);
        var book = new TunerHardwareBook(Configuration, clock);
        var presence = new ScriptedPresence();
        TunerPatrol patrol = Patrol(book, presence, clock, busy: []);

        presence.Missing.Add("adapter0.frontend0");
        patrol.Walk();
        presence.Missing.Clear();
        patrol.Walk();

        Assert.True(book.For(Configuration.Devices![0]).Present);
    }

    [Fact]
    public void ATunerASessionHoldsIsNotLookedAt()
    {
        var clock = new ManualTimeProvider(Start);
        var book = new TunerHardwareBook(Configuration, clock);
        var presence = new ScriptedPresence();

        Patrol(book, presence, clock, busy: ["adapter0.frontend0"]).Walk();

        Assert.Equal(["adapter1.frontend0"], presence.Asked);
        Assert.Null(book.For(Configuration.Devices![0]).Present);
    }

    [Fact]
    public void ADetectionCountsAsSeeingTheTuner()
    {
        var clock = new ManualTimeProvider(Start);
        var book = new TunerHardwareBook(Configuration, clock);

        book.Remember([
            new TunerDetection(
                "adapter0.frontend0",
                [DeviceKind.Terrestrial],
                DeviceDetection.Busy,
                null,
                "/dev/dvb/adapter0/frontend0"
            ),
        ]);

        TunerHardwareDto hardware = book.For(Configuration.Devices![0]);
        Assert.True(hardware.Present);
        Assert.Equal(Start, hardware.LastSeenAt);
    }

    [Fact]
    public void ThePatrolGoesRoundOnceAMinute()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), TunerPatrol.Interval);
    }

    private static TunerPatrol Patrol(
        TunerHardwareBook book,
        ITunerPresence presence,
        TimeProvider clock,
        IReadOnlyList<string> busy
    ) =>
        new(Configuration, book, presence, () => busy, clock);

    private sealed class ScriptedPresence : ITunerPresence
    {
        public HashSet<string> Missing { get; } = new(StringComparer.Ordinal);

        public List<string> Asked { get; } = [];

        public bool IsPresent(DeviceSettings device, TunerHardwareDto hardware)
        {
            Asked.Add(device.Id!);

            return !Missing.Contains(device.Id!);
        }
    }
}

public sealed class TunerPresenceApiTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task ATunerSaysWhetherItIsThereAndWhenItWasLastSeen()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage response = await client.GetAsync(
            DriverEndpoints.Tuners,
            new CancellationTokenSource(Patience).Token
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IReadOnlyList<TunerSnapshot>? tuners = await DriverUnderTest.Read(
            response,
            DriverJson.Context.IReadOnlyListTunerSnapshot
        );

        Assert.All(tuners ?? [], tuner =>
        {
            Assert.True(tuner.Hardware?.Present);
            Assert.NotNull(tuner.Hardware?.LastSeenAt);
        });
    }
}
