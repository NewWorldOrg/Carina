namespace Carina.Driver.Tuning.Usb;

public readonly record struct UsbSetup(byte RequestType, byte Request, ushort Value, ushort Index);

public readonly record struct UsbModel(ushort VendorId, ushort ProductId)
{
    public override string ToString() => $"{VendorId:X4}:{ProductId:X4}";
}

/// <param name="Port">Where the device hangs on the bus, which stays put across a replug while the node does not.</param>
public sealed record UsbDeviceNode(string Path, UsbModel Model, string Port);

/// <summary>
/// The operating system's way to a USB device: the nodes it can see, and an opened device with one interface claimed.
/// </summary>
public interface IUsbHost
{
    IReadOnlyList<UsbDeviceNode> List();

    UsbModel ModelAt(string path);

    IUsbConnection Open(string path, byte interfaceNumber);
}

public interface IUsbConnection : IDisposable
{
    string Path { get; }

    int ControlIn(UsbSetup setup, Span<byte> data, TimeSpan timeout);

    void ControlOut(UsbSetup setup, ReadOnlySpan<byte> data, TimeSpan timeout);

    /// <summary>
    /// Puts <paramref name="transfersInFlight" /> reads of <paramref name="transferBytes" /> on the endpoint and keeps
    /// them there, so the device never finds the host without a read waiting.
    /// </summary>
    IUsbBulkReader ReadBulk(byte endpoint, int transferBytes, int transfersInFlight);
}

public interface IUsbBulkReader : IDisposable
{
    /// <summary>
    /// Copies the next finished read into <paramref name="into" /> and puts the read back on the endpoint. Answers 0
    /// when none finished within <paramref name="patience" />; the reads stay where they are.
    /// </summary>
    int Take(Span<byte> into, TimeSpan patience);
}
