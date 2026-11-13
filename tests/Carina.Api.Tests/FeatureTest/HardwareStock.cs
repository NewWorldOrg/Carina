using Carina.TestSupport;

using Puppis.Contracts;

namespace Carina.Api.Tests.FeatureTest;

/// <summary>
/// A driver that answers with the hardware behind its tuners, its detected devices and its card reader.
/// </summary>
internal static class HardwareStock
{
    public static readonly DateTimeOffset Moment = new(2026, 11, 1, 19, 20, 0, TimeSpan.FromHours(9));

    private static readonly TunerHardwareDto Hardware = new()
    {
        Name = "Example ISDB-T frontend",
        KernelDriver = "example_dvb",
        Bus = TunerBus.Pci,
        DevicePath = "/dev/dvb/adapter0/frontend0",
        Present = true,
        LastSeenAt = Moment,
    };

    public static DriverHello Capable() =>
        FakeDriver.HelloFor(
            "instance-a",
            capabilities:
            [
                DriverCapabilities.TunerLedger,
                DriverCapabilities.DeviceDetection,
                DriverCapabilities.DeviceHardware,
                DriverCapabilities.CardReaderStatus,
            ]);

    public static void Stocked(FakeDriver driver)
    {
        driver.Ledger = new TunerLedgerDto
        {
            Tuners = [new TunerConfigEntry { DeviceId = "adapter0" }, new TunerConfigEntry { DeviceId = "adapter1" }],
            SavedHash = "saved",
            LoadedHash = "saved",
        };
        driver.Tuners =
        [
            new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Idle) { Hardware = Hardware },
            new TunerSnapshot("adapter1", TunerKind.Terrestrial, TunerState.Idle),
        ];
        driver.DetectedDevices =
        [
            new DetectedDeviceDto
            {
                DeviceId = "adapter0",
                Detection = DeviceDetection.Detected,
                Kinds = [TunerKind.Terrestrial],
                Hardware = Hardware,
            },
        ];
    }
}
