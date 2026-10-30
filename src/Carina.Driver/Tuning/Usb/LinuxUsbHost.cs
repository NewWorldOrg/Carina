using System.Buffers.Binary;
using System.Runtime.InteropServices;

using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tuning.Usb;

/// <summary>
/// Reaches USB devices through usbdevfs, the nodes under /dev/bus/usb, without a library between.
/// </summary>
public sealed partial class LinuxUsbHost : IUsbHost
{
    public const string NodeRoot = "/dev/bus/usb/";

    private const string DeviceTree = "/sys/bus/usb/devices/";
    private const int ReadOnlyFlag = 0;
    private const int ReadWriteFlag = 2;
    private const int CloseOnExecFlag = 0x80000;
    private const byte DeviceDescriptorType = 1;

    public LinuxUsbHost()
    {
        if (!UsbdevfsLayout.DescribesThisMachine)
        {
            throw DvbFailure.Refused(
                $"tuner.backend: the usb backend only knows the kernel structure layout for 64 bit little endian machines, and this process is {RuntimeInformation.ProcessArchitecture}, so the driver will not guess at it."
            );
        }
    }

    public IReadOnlyList<UsbDeviceNode> List()
    {
        if (!Directory.Exists(NodeRoot))
        {
            return [];
        }

        IReadOnlyDictionary<string, string> ports = PortsByNode();
        var nodes = new List<UsbDeviceNode>();

        foreach (string bus in Directory.EnumerateDirectories(NodeRoot).Order(StringComparer.Ordinal))
        {
            foreach (string node in Directory.EnumerateFiles(bus).Order(StringComparer.Ordinal))
            {
                if (TryReadModel(node, out UsbModel model, out _))
                {
                    string key = NodeKey(node);
                    nodes.Add(new UsbDeviceNode(node, model, ports.GetValueOrDefault(key, key)));
                }
            }
        }

        return nodes;
    }

    public UsbModel ModelAt(string path) =>
        TryReadModel(path, out UsbModel model, out int error)
            ? model
            : throw DvbFailure.AtDevice(
                path,
                "reading the device descriptor",
                error,
                "Without it the driver cannot tell which device this node is."
            );

    public IUsbConnection Open(string path, byte interfaceNumber)
    {
        int descriptor = OpenDescriptor(path, ReadWriteFlag | CloseOnExecFlag);

        if (descriptor < 0)
        {
            throw DvbFailure.AtDevice(
                path,
                "opening the device for reading and writing",
                Marshal.GetLastPInvokeError(),
                "The node exists, but this process may not drive it; check the owner and the mode of the node."
            );
        }

        if (!LinuxUsbConnection.Claim(descriptor, interfaceNumber, out int error))
        {
            CloseDescriptor(descriptor);

            throw DvbFailure.AtDevice(
                path,
                $"claiming interface {interfaceNumber}",
                error,
                "Another process or a kernel driver holds the device."
            );
        }

        return new LinuxUsbConnection(path, descriptor, interfaceNumber);
    }

    private static unsafe bool TryReadModel(string path, out UsbModel model, out int error)
    {
        model = default;
        error = 0;
        int descriptor = OpenDescriptor(path, ReadOnlyFlag | CloseOnExecFlag);

        if (descriptor < 0)
        {
            error = Marshal.GetLastPInvokeError();

            return false;
        }

        byte[] block = new byte[UsbdevfsLayout.DescriptorBytes];
        nint read;

        fixed (byte* pointer = block)
        {
            read = ReadDescriptor(descriptor, pointer, (nuint)block.Length);
        }

        error = read < 0 ? Marshal.GetLastPInvokeError() : 0;
        CloseDescriptor(descriptor);

        if (read < block.Length || block[1] is not DeviceDescriptorType)
        {
            return false;
        }

        model = new UsbModel(
            BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(UsbdevfsLayout.DescriptorVendorAt)),
            BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(UsbdevfsLayout.DescriptorProductAt))
        );

        return true;
    }

    private static IReadOnlyDictionary<string, string> PortsByNode()
    {
        var ports = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!Directory.Exists(DeviceTree))
        {
            return ports;
        }

        foreach (string device in Directory.EnumerateDirectories(DeviceTree))
        {
            string name = Path.GetFileName(device);

            if (name.Contains(':', StringComparison.Ordinal) || !char.IsAsciiDigit(name[0]))
            {
                continue;
            }

            if (TryReadNumber(device, "busnum", out int bus) && TryReadNumber(device, "devnum", out int number))
            {
                ports[$"{bus:000}-{number:000}"] = name;
            }
        }

        return ports;
    }

    private static bool TryReadNumber(string device, string attribute, out int value)
    {
        value = 0;

        try
        {
            return int.TryParse(File.ReadAllText(Path.Combine(device, attribute)).Trim(), out value);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string NodeKey(string node)
    {
        string bus = Path.GetFileName(Path.GetDirectoryName(node)) ?? string.Empty;

        return $"{bus}-{Path.GetFileName(node)}";
    }

    [LibraryImport(
        "libc",
        EntryPoint = "open",
        StringMarshalling = StringMarshalling.Utf8,
        SetLastError = true
    )]
    private static partial int OpenDescriptor(string path, int flags);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int CloseDescriptor(int descriptor);

    [LibraryImport("libc", EntryPoint = "read", SetLastError = true)]
    private static unsafe partial nint ReadDescriptor(int descriptor, void* buffer, nuint count);
}
