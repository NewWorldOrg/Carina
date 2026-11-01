using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Tuning.Dvb;
using Carina.Driver.Tuning.Usb;

namespace Carina.Driver.Tuning;

public sealed record TunerDetection(
    string DeviceId,
    IReadOnlyList<DeviceKind> Receives,
    DeviceDetection Detection,
    string? Detail,
    string? DevicePath = null,
    DeviceHardware? Hardware = null
);

/// <param name="Name">The name the hardware gives itself, or <see langword="null"/> when it could not be asked.</param>
/// <param name="KernelDriver">The kernel driver the device is bound to, when one is.</param>
/// <param name="UsbPort">Where a USB device hangs on the bus.</param>
public sealed record DeviceHardware(string? Name, string? KernelDriver, TunerBus Bus, string? UsbPort);

public interface ITunerDetector
{
    IReadOnlyList<TunerDetection> Detect();
}

public sealed class FakeTunerDetector(DriverConfiguration configuration) : ITunerDetector
{
    public IReadOnlyList<TunerDetection> Detect() =>
    [
        .. (configuration.Devices ?? [])
            .Where(device => device?.Id is not null)
            .Select(Describe),
    ];

    private static TunerDetection Describe(DeviceSettings device)
    {
        IReadOnlyList<DeviceKind> receives =
            device.Kind is DeviceKind.Unspecified ? [] : [device.Kind];

        return new TunerDetection(
            device.Id!,
            receives,
            receives.Count is 0 ? DeviceDetection.Unreadable : DeviceDetection.Detected,
            receives.Count is 0
                ? "The configuration does not say what this synthetic tuner receives."
                : null,
            device.DevicePath,
            new DeviceHardware(null, null, TunerBus.Synthetic, null)
        );
    }
}

public static class TunerDetectors
{
    public static ITunerDetector For(DriverConfiguration configuration) =>
        (configuration.Tuner?.Backend ?? TunerBackend.Unspecified) switch
        {
            TunerBackend.Fake => new FakeTunerDetector(configuration),
            TunerBackend.Dvb => new DvbTunerDetector(),
            TunerBackend.Usb => new UsbTunerDetector(),
            _ => throw new InvalidOperationException(
                "The tuner backend was never established; the configuration should have been rejected."
            ),
        };
}
