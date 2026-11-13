using Carina.TestSupport;

using Puppis.Contracts;

namespace Carina.Api.Tests.Feature.Tuners;

internal static class TunerLedgerFeature
{
    public static readonly Uri TunersPath = new("/api/tuners", UriKind.Relative);

    public static readonly Uri Detected = new("/api/tuners/detected", UriKind.Relative);

    public static readonly string[] Everything =
    [
        DriverCapabilities.TunerLedger,
        DriverCapabilities.DeviceDetection,
        DriverCapabilities.LiveTunerToggle,
    ];

    public static readonly DateTimeOffset Started =
        new(2026, 8, 12, 21, 0, 0, TimeSpan.FromHours(9));

    public static DriverHello Capable(string[]? capabilities = null)
        => FakeDriver.HelloFor("instance-a", capabilities: capabilities ?? Everything);

    public static void Stocked(FakeDriver driver)
    {
        driver.Ledger = new TunerLedgerDto
        {
            Tuners =
            [
                new TunerConfigEntry { DeviceId = "adapter0" },
                new TunerConfigEntry { DeviceId = "adapter1", Disabled = true },
            ],
            SavedHash = "saved",
            LoadedHash = "saved",
        };

        driver.Tuners =
        [
            new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Idle),
            new TunerSnapshot("adapter1", TunerKind.Satellite, TunerState.Disabled),
        ];

        driver.DetectedDevices =
        [
            new DetectedDeviceDto
            {
                DeviceId = "adapter0",
                Detection = DeviceDetection.Detected,
                Kinds = [TunerKind.Terrestrial],
            },
            new DetectedDeviceDto
            {
                DeviceId = "adapter1",
                Detection = DeviceDetection.Detected,
                Kinds = [TunerKind.Satellite],
            },
        ];
    }

    public static void Unconfigured(FakeDriver driver)
    {
        driver.Ledger = new TunerLedgerDto { SavedHash = "empty", LoadedHash = "empty" };

        driver.Tuners = [];

        driver.DetectedDevices =
        [
            new DetectedDeviceDto
            {
                DeviceId = "adapter0",
                Detection = DeviceDetection.Detected,
                Kinds = [TunerKind.Terrestrial],
            },
            new DetectedDeviceDto
            {
                DeviceId = "adapter1",
                Detection = DeviceDetection.Detected,
                Kinds = [TunerKind.Satellite],
            },
        ];
    }
}
