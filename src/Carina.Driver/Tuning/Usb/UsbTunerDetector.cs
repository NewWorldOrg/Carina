using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Tuning.Usb.CechZd1;

namespace Carina.Driver.Tuning.Usb;

/// <summary>
/// Finds the USB tuners this driver knows among the devices the host can read. The device id follows the port the
/// tuner hangs on, which a replug into the same socket keeps.
/// </summary>
public sealed class UsbTunerDetector : ITunerDetector
{
    private readonly Lazy<IUsbHost> host;

    public UsbTunerDetector()
        : this(new Lazy<IUsbHost>(() => new LinuxUsbHost())) { }

    private UsbTunerDetector(Lazy<IUsbHost> host)
    {
        this.host = host;
    }

    public static UsbTunerDetector Using(IUsbHost host) => new(new Lazy<IUsbHost>(() => host));

    public IReadOnlyList<TunerDetection> Detect() =>
    [
        .. host.Value.List()
            .Where(node => node.Model == CechZd1TunerDevice.Model)
            .Select(node => new TunerDetection(
                $"usb{node.Port}",
                [DeviceKind.Terrestrial],
                DeviceDetection.Detected,
                null,
                node.Path
            )),
    ];
}
