using System.Buffers.Binary;
using System.Runtime.InteropServices;

using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tuning.Usb;

public sealed partial class LinuxUsbConnection : IUsbConnection
{
    private const int TryAgain = 11;

    private readonly int descriptor;
    private readonly byte interfaceNumber;
    private readonly List<Action> afterClosing = [];
    private bool closed;

    internal LinuxUsbConnection(string path, int descriptor, byte interfaceNumber)
    {
        Path = path;
        this.descriptor = descriptor;
        this.interfaceNumber = interfaceNumber;
    }

    public string Path { get; }

    internal int Descriptor => descriptor;

    internal static unsafe bool Claim(int descriptor, byte interfaceNumber, out int error)
    {
        uint number = interfaceNumber;
        int result = IoctlPointer(descriptor, UsbdevfsLayout.ClaimInterface, &number);
        error = result < 0 ? Marshal.GetLastPInvokeError() : 0;

        return result >= 0;
    }

    public unsafe int ControlIn(UsbSetup setup, Span<byte> data, TimeSpan timeout)
    {
        fixed (byte* pointer = data)
        {
            return Control(setup, pointer, data.Length, timeout, "reading from the control endpoint");
        }
    }

    public unsafe void ControlOut(UsbSetup setup, ReadOnlySpan<byte> data, TimeSpan timeout)
    {
        fixed (byte* pointer = data)
        {
            int sent = Control(setup, pointer, data.Length, timeout, "writing to the control endpoint");

            if (sent != data.Length)
            {
                throw DvbFailure.Refused(
                    $"{Path}: the device took {sent} of the {data.Length} bytes written to its control endpoint."
                );
            }
        }
    }

    public IUsbBulkReader ReadBulk(byte endpoint, int transferBytes, int transfersInFlight) =>
        new LinuxUsbBulkReader(this, endpoint, transferBytes, transfersInFlight);

    public unsafe void Dispose()
    {
        if (closed)
        {
            return;
        }

        closed = true;
        uint number = interfaceNumber;
        IoctlPointer(descriptor, UsbdevfsLayout.ReleaseInterface, &number);
        CloseDescriptor(descriptor);

        foreach (Action release in afterClosing)
        {
            release();
        }
    }

    internal void AfterClosing(Action release) => afterClosing.Add(release);

    internal static DvbDeviceException Failed(string path, string operation, int error) =>
        DvbFailure.AtDevice(
            path,
            operation,
            error,
            error is Errno.NoSuchDevice
                ? "The device has gone from the bus; it is used again once it is plugged back in."
                : "The device stopped answering the host."
        );

    internal unsafe bool Submit(byte* urb, out int error)
    {
        int result = IoctlPointer(descriptor, UsbdevfsLayout.SubmitUrb, urb);
        error = result < 0 ? Marshal.GetLastPInvokeError() : 0;

        return result >= 0;
    }

    internal unsafe void Discard(byte* urb) => IoctlPointer(descriptor, UsbdevfsLayout.DiscardUrb, urb);

    internal unsafe byte* Reap(out int error)
    {
        byte* reaped = null;
        int result = IoctlPointer(descriptor, UsbdevfsLayout.ReapUrbWithoutWaiting, &reaped);
        error = result < 0 ? Marshal.GetLastPInvokeError() : 0;

        return result < 0 ? null : reaped;
    }

    internal static bool NothingToReap(int error) => error is TryAgain;

    internal unsafe int WaitForFinishedReads(int timeoutMilliseconds, out int error)
    {
        PollRequest request = new() { Descriptor = descriptor, Events = PollRequest.Writable };
        int result = PollDescriptors(&request, 1, timeoutMilliseconds);
        error = result < 0 ? Marshal.GetLastPInvokeError() : 0;

        if (result > 0 && (request.Returned & PollRequest.Gone) is not 0)
        {
            error = Errno.NoSuchDevice;

            return -1;
        }

        return result;
    }

    private unsafe int Control(UsbSetup setup, byte* data, int length, TimeSpan timeout, string operation)
    {
        byte* block = stackalloc byte[UsbdevfsLayout.ControlBytes];
        Span<byte> fields = new(block, UsbdevfsLayout.ControlBytes);
        fields.Clear();
        fields[UsbdevfsLayout.ControlRequestTypeAt] = setup.RequestType;
        fields[UsbdevfsLayout.ControlRequestAt] = setup.Request;
        BinaryPrimitives.WriteUInt16LittleEndian(fields[UsbdevfsLayout.ControlValueAt..], setup.Value);
        BinaryPrimitives.WriteUInt16LittleEndian(fields[UsbdevfsLayout.ControlIndexAt..], setup.Index);
        BinaryPrimitives.WriteUInt16LittleEndian(fields[UsbdevfsLayout.ControlLengthAt..], (ushort)length);
        BinaryPrimitives.WriteUInt32LittleEndian(
            fields[UsbdevfsLayout.ControlTimeoutAt..],
            (uint)timeout.TotalMilliseconds
        );
        BinaryPrimitives.WriteInt64LittleEndian(fields[UsbdevfsLayout.ControlDataAt..], (long)data);

        int result = IoctlPointer(descriptor, UsbdevfsLayout.Control, block);

        return result < 0 ? throw Failed(Path, operation, Marshal.GetLastPInvokeError()) : result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PollRequest
    {
        public const short Writable = 0x004;
        public const short Gone = 0x008 | 0x010;

        public int Descriptor;
        public short Events;
        public short Returned;
    }

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int CloseDescriptor(int descriptor);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static unsafe partial int IoctlPointer(int descriptor, nuint request, void* argument);

    [LibraryImport("libc", EntryPoint = "poll", SetLastError = true)]
    private static unsafe partial int PollDescriptors(void* descriptors, nuint count, int timeoutMilliseconds);
}
