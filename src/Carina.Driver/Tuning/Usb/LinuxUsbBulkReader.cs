using System.Diagnostics;
using System.Runtime.InteropServices;

using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tuning.Usb;

/// <summary>
/// Keeps a fixed set of bulk reads submitted on one endpoint. The reads and their buffers live outside the managed
/// heap because the kernel writes into them while they are submitted.
/// </summary>
internal sealed unsafe class LinuxUsbBulkReader : IUsbBulkReader
{
    private const int ShutDown = 108;
    private const int PollMilliseconds = 100;

    private static readonly TimeSpan DiscardPatience = TimeSpan.FromSeconds(2);

    private readonly LinuxUsbConnection connection;
    private readonly int transferBytes;
    private readonly byte* urbs;
    private readonly byte*[] buffers;
    private readonly bool[] submitted;
    private bool closed;

    public LinuxUsbBulkReader(LinuxUsbConnection connection, byte endpoint, int transferBytes, int transfersInFlight)
    {
        this.connection = connection;
        this.transferBytes = transferBytes;
        urbs = (byte*)NativeMemory.AllocZeroed((nuint)(UsbdevfsLayout.UrbBytes * transfersInFlight));
        buffers = new byte*[transfersInFlight];
        submitted = new bool[transfersInFlight];

        for (int index = 0; index < transfersInFlight; index++)
        {
            buffers[index] = (byte*)NativeMemory.Alloc((nuint)transferBytes);
            byte* urb = UrbAt(index);
            urb[UsbdevfsLayout.UrbTypeAt] = UsbdevfsLayout.BulkUrb;
            urb[UsbdevfsLayout.UrbEndpointAt] = endpoint;
            *(long*)(urb + UsbdevfsLayout.UrbBufferAt) = (long)buffers[index];
            *(int*)(urb + UsbdevfsLayout.UrbBufferLengthAt) = transferBytes;
            *(long*)(urb + UsbdevfsLayout.UrbUserContextAt) = index;
        }

        try
        {
            for (int index = 0; index < transfersInFlight; index++)
            {
                SubmitOrThrow(index);
            }
        }
        catch
        {
            Dispose();

            throw;
        }
    }

    public int Take(Span<byte> into, TimeSpan patience)
    {
        ObjectDisposedException.ThrowIf(closed, this);

        if (into.Length < transferBytes)
        {
            throw new ArgumentException($"A read can carry {transferBytes} bytes; the room given is {into.Length}.");
        }

        int ready = connection.WaitForFinishedReads((int)patience.TotalMilliseconds, out int waitError);

        if (ready < 0 && waitError is not Errno.Interrupted)
        {
            throw LinuxUsbConnection.Failed(connection.Path, "waiting for a bulk read", waitError);
        }

        if (ready <= 0)
        {
            return 0;
        }

        byte* urb = connection.Reap(out int reapError);

        if (urb is null)
        {
            return LinuxUsbConnection.NothingToReap(reapError)
                ? 0
                : throw LinuxUsbConnection.Failed(connection.Path, "taking a finished bulk read", reapError);
        }

        int index = (int)*(long*)(urb + UsbdevfsLayout.UrbUserContextAt);
        submitted[index] = false;
        int status = *(int*)(urb + UsbdevfsLayout.UrbStatusAt);
        int arrived = *(int*)(urb + UsbdevfsLayout.UrbActualLengthAt);

        if (-status is Errno.NoSuchDevice or ShutDown)
        {
            throw LinuxUsbConnection.Failed(connection.Path, "reading the bulk endpoint", -status);
        }

        int delivered = status is 0 ? Math.Clamp(arrived, 0, transferBytes) : 0;
        new ReadOnlySpan<byte>(buffers[index], delivered).CopyTo(into);
        SubmitOrThrow(index);

        return delivered;
    }

    public void Dispose()
    {
        if (closed)
        {
            return;
        }

        closed = true;

        for (int index = 0; index < submitted.Length; index++)
        {
            if (submitted[index])
            {
                connection.Discard(UrbAt(index));
            }
        }

        if (ReapTheDiscarded())
        {
            Free();
        }
        else
        {
            connection.AfterClosing(Free);
        }
    }

    /// <summary>
    /// Waits for every discarded read to come back. Memory the kernel may still write into is not freed until the
    /// device is closed, which is when the kernel lets go of the reads it still holds.
    /// </summary>
    private bool ReapTheDiscarded()
    {
        long started = Stopwatch.GetTimestamp();

        while (submitted.Contains(true))
        {
            byte* urb = connection.Reap(out int error);

            if (urb is not null)
            {
                submitted[(int)*(long*)(urb + UsbdevfsLayout.UrbUserContextAt)] = false;

                continue;
            }

            if (!LinuxUsbConnection.NothingToReap(error) || Stopwatch.GetElapsedTime(started) > DiscardPatience)
            {
                return false;
            }

            connection.WaitForFinishedReads(PollMilliseconds, out _);
        }

        return true;
    }

    private void Free()
    {
        for (int index = 0; index < buffers.Length; index++)
        {
            NativeMemory.Free(buffers[index]);
        }

        NativeMemory.Free(urbs);
    }

    private void SubmitOrThrow(int index)
    {
        byte* urb = UrbAt(index);
        *(int*)(urb + UsbdevfsLayout.UrbStatusAt) = 0;
        *(int*)(urb + UsbdevfsLayout.UrbActualLengthAt) = 0;

        if (!connection.Submit(urb, out int error))
        {
            throw LinuxUsbConnection.Failed(connection.Path, "putting a bulk read on the endpoint", error);
        }

        submitted[index] = true;
    }

    private byte* UrbAt(int index) => urbs + (index * UsbdevfsLayout.UrbBytes);
}
