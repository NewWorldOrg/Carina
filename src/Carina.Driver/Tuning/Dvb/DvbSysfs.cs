using Carina.Contracts;

namespace Carina.Driver.Tuning.Dvb;

public sealed record DvbSysfsEntry(string? KernelDriver, TunerBus Bus)
{
    public static readonly DvbSysfsEntry Unfiled = new(null, TunerBus.Unspecified);
}

/// <summary>
/// Reads where the kernel filed a DVB frontend: <c>dvb{adapter}.frontend{n}</c> under the class directory, whose
/// <c>device</c> link leads to the kernel driver it is bound to and the bus it sits on. Nothing there is opened.
/// </summary>
public sealed class DvbSysfs(string classRoot)
{
    public const string ClassRoot = "/sys/class/dvb";

    private const string AdapterPrefix = "adapter";

    public DvbSysfsEntry Read(string frontendPath)
    {
        if (EntryFor(frontendPath) is not { } entry)
        {
            return DvbSysfsEntry.Unfiled;
        }

        string device = Path.Combine(classRoot, entry, "device");

        return new DvbSysfsEntry(LinkedName(Path.Combine(device, "driver")), BusOf(LinkedName(Path.Combine(device, "subsystem"))));
    }

    private static string? EntryFor(string frontendPath)
    {
        string frontend = Path.GetFileName(frontendPath);
        string adapter = Path.GetFileName(Path.GetDirectoryName(frontendPath) ?? string.Empty);

        return adapter.StartsWith(AdapterPrefix, StringComparison.Ordinal) && frontend.Length > 0
            ? $"dvb{adapter[AdapterPrefix.Length..]}.{frontend}"
            : null;
    }

    private static string? LinkedName(string link)
    {
        try
        {
            string? target = new DirectoryInfo(link).LinkTarget;

            return target is null ? null : Path.GetFileName(target.TrimEnd('/'));
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

    private static TunerBus BusOf(string? subsystem) =>
        subsystem switch
        {
            "pci" => TunerBus.Pci,
            "usb" => TunerBus.Usb,
            _ => TunerBus.Unspecified,
        };
}
