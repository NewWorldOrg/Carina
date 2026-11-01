using Carina.Contracts;
using Carina.Driver.Configuration;

namespace Carina.Driver.Tuning;

/// <summary>
/// What each tuner was found to be, kept from the last detection that named it. A frontend another process holds
/// cannot be asked its name, so a name read once stays until a later detection reads another.
/// </summary>
public sealed class TunerHardwareBook(DriverConfiguration configuration, TimeProvider clock)
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, TunerHardwareDto> known = new(StringComparer.Ordinal);

    public void Remember(IReadOnlyList<TunerDetection> detections)
    {
        DateTimeOffset now = clock.GetUtcNow();

        lock (gate)
        {
            foreach (TunerDetection detection in detections)
            {
                TunerHardwareDto found = Of(detection) with { Present = true, LastSeenAt = now };

                known[detection.DeviceId] = known.TryGetValue(detection.DeviceId, out TunerHardwareDto? before)
                    ? Merged(before, found)
                    : found;
            }
        }
    }

    /// <summary>
    /// Writes down what a look at the device's place found. A device that is not there keeps when it was last seen.
    /// </summary>
    public void Looked(DeviceSettings device, bool present)
    {
        if (device.Id is not { } deviceId)
        {
            return;
        }

        DateTimeOffset now = clock.GetUtcNow();

        lock (gate)
        {
            TunerHardwareDto before = known.GetValueOrDefault(deviceId) ?? FromConfiguration(device);

            known[deviceId] = present
                ? before with { Present = true, LastSeenAt = now }
                : before with { Present = false };
        }
    }

    public TunerHardwareDto? Known(string deviceId)
    {
        lock (gate)
        {
            return known.GetValueOrDefault(deviceId);
        }
    }

    public TunerHardwareDto For(DeviceSettings device) =>
        (device.Id is { } deviceId ? Known(deviceId) : null) ?? FromConfiguration(device);

    private TunerHardwareDto FromConfiguration(DeviceSettings device) =>
        new()
        {
            Bus = (configuration.Tuner?.Backend ?? TunerBackend.Unspecified) switch
            {
                TunerBackend.Fake => TunerBus.Synthetic,
                TunerBackend.Usb => TunerBus.Usb,
                _ => TunerBus.Unspecified,
            },
            DevicePath = device.DevicePath,
        };

    private static TunerHardwareDto Of(TunerDetection detection) =>
        new()
        {
            Name = detection.Hardware?.Name,
            KernelDriver = detection.Hardware?.KernelDriver,
            Bus = detection.Hardware?.Bus ?? TunerBus.Unspecified,
            DevicePath = detection.DevicePath,
            UsbPort = detection.Hardware?.UsbPort,
        };

    private static TunerHardwareDto Merged(TunerHardwareDto before, TunerHardwareDto found) =>
        new()
        {
            Name = found.Name ?? before.Name,
            KernelDriver = found.KernelDriver ?? before.KernelDriver,
            Bus = found.Bus is TunerBus.Unspecified ? before.Bus : found.Bus,
            DevicePath = found.DevicePath ?? before.DevicePath,
            UsbPort = found.UsbPort ?? before.UsbPort,
            Present = found.Present ?? before.Present,
            LastSeenAt = found.LastSeenAt ?? before.LastSeenAt,
        };
}
