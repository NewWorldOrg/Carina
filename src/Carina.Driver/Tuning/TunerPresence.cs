using System.Globalization;

using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Tuning.Usb;
using Carina.Driver.Tuning.Usb.CechZd1;

namespace Carina.Driver.Tuning;

/// <summary>
/// Says whether a tuner is in its place without opening it, so looking never disturbs a device.
/// </summary>
public interface ITunerPresence
{
    bool IsPresent(DeviceSettings device, TunerHardwareDto hardware);
}

public sealed class DvbPresence : ITunerPresence
{
    public bool IsPresent(DeviceSettings device, TunerHardwareDto hardware) =>
        (hardware.DevicePath ?? device.DevicePath) is { } path && File.Exists(path);
}

/// <summary>
/// Looks for the tuner's port among the devices the kernel lists under <c>/sys/bus/usb/devices</c>, and for the
/// tuner's model in what the kernel says that port holds.
/// </summary>
public sealed class UsbPresence(string deviceTree) : ITunerPresence
{
    public const string DeviceTree = "/sys/bus/usb/devices";

    private const string IdPrefix = "usb";

    public UsbPresence()
        : this(DeviceTree) { }

    public bool IsPresent(DeviceSettings device, TunerHardwareDto hardware)
    {
        if (PortOf(device, hardware) is not { } port)
        {
            return false;
        }

        string directory = Path.Combine(deviceTree, port);

        return Directory.Exists(directory)
            && Hex(Path.Combine(directory, "idVendor")) == CechZd1TunerDevice.Model.VendorId
            && Hex(Path.Combine(directory, "idProduct")) == CechZd1TunerDevice.Model.ProductId;
    }

    private static string? PortOf(DeviceSettings device, TunerHardwareDto hardware)
    {
        if (hardware.UsbPort is { Length: > 0 } port)
        {
            return port;
        }

        return device.Id is { } id && id.StartsWith(IdPrefix, StringComparison.Ordinal) && id.Length > IdPrefix.Length
            ? id[IdPrefix.Length..]
            : null;
    }

    private static ushort? Hex(string file)
    {
        try
        {
            return ushort.TryParse(File.ReadAllText(file).Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort value)
                ? value
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}

public sealed class SyntheticPresence : ITunerPresence
{
    public bool IsPresent(DeviceSettings device, TunerHardwareDto hardware) => true;
}

public static class TunerPresences
{
    public static ITunerPresence For(DriverConfiguration configuration) =>
        (configuration.Tuner?.Backend ?? TunerBackend.Unspecified) switch
        {
            TunerBackend.Dvb => new DvbPresence(),
            TunerBackend.Usb => new UsbPresence(),
            _ => new SyntheticPresence(),
        };
}
