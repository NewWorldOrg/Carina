using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Carina.Driver.Tuning.Dvb;

public readonly record struct SyscallOutcome(int Value, int Error)
{
    public static SyscallOutcome Ok(int value) => new(value, 0);

    public static SyscallOutcome Failed(int error) => new(-1, error);

    public bool Refused => Value < 0;
}

public enum DvbAccess
{
    Unspecified = 0,

    Inspect = 1,

    Control = 2,

    Stream = 3,
}

public enum LnbVoltage
{
    Thirteen = 0,

    Eighteen = 1,

    Off = 2,
}

public interface IDvbSystemCalls
{
    SyscallOutcome Open(string path, DvbAccess access);

    SyscallOutcome Close(int descriptor);

    SyscallOutcome SetProperties(int descriptor, byte[] records);

    SyscallOutcome GetProperties(int descriptor, byte[] records);

    SyscallOutcome ReadStatus(int descriptor, out uint flags);

    SyscallOutcome ReadFrontendInfo(int descriptor, byte[] block);

    SyscallOutcome SetLnbVoltage(int descriptor, LnbVoltage voltage);

    SyscallOutcome SetPesFilter(int descriptor, byte[] filter);

    SyscallOutcome SetBufferSize(int descriptor, int bytes);

    SyscallOutcome StopFilter(int descriptor);

    SyscallOutcome ReadBytes(int descriptor, byte[] buffer, int count);

    SyscallOutcome WaitForReadable(int descriptor, int timeoutMilliseconds);

    void Rest(TimeSpan interval, CancellationToken cancellationToken);
}

public sealed partial class LinuxDvbSystemCalls : IDvbSystemCalls
{
    private const int ReadOnlyFlag = 0;
    private const int ReadWriteFlag = 2;
    private const int NonBlockingFlag = 0x800;

    private const short ReadableEvent = 0x001;
    private const nuint OneDescriptor = 1;

    public LinuxDvbSystemCalls()
    {
        if (!DvbLayout.DescribesThisMachine)
        {
            throw DvbFailure.Refused(
                $"tuner.backend: the dvb backend only knows the kernel structure layout for 64 bit little endian machines, and this process is {RuntimeInformation.ProcessArchitecture}, so the driver will not guess at it."
            );
        }
    }

    public SyscallOutcome Open(string path, DvbAccess access)
    {
        var flags = access switch
        {
            DvbAccess.Inspect => ReadOnlyFlag,
            DvbAccess.Control => ReadWriteFlag,
            DvbAccess.Stream => ReadOnlyFlag | NonBlockingFlag,
            _ => throw DvbFailure.Refused(
                $"access: '{access}' does not say how the driver should open '{path}'."
            ),
        };

        return Outcome(OpenDescriptor(path, flags));
    }

    public SyscallOutcome Close(int descriptor) => Outcome(CloseDescriptor(descriptor));

    public SyscallOutcome SetProperties(int descriptor, byte[] records) =>
        WithPropertyHeader(descriptor, DvbIoctl.FrontendSetProperty, records);

    public SyscallOutcome GetProperties(int descriptor, byte[] records) =>
        WithPropertyHeader(descriptor, DvbIoctl.FrontendGetProperty, records);

    public unsafe SyscallOutcome ReadStatus(int descriptor, out uint flags)
    {
        var block = new byte[DvbLayout.FrontendStatusBytes];

        fixed (byte* pointer = block)
        {
            var outcome = Outcome(
                IoctlPointer(descriptor, DvbIoctl.FrontendReadStatus, pointer)
            );

            flags = outcome.Refused ? 0 : BinaryPrimitives.ReadUInt32LittleEndian(block);

            return outcome;
        }
    }

    public unsafe SyscallOutcome ReadFrontendInfo(int descriptor, byte[] block)
    {
        fixed (byte* pointer = block)
        {
            return Outcome(IoctlPointer(descriptor, DvbIoctl.FrontendGetInfo, pointer));
        }
    }

    public SyscallOutcome SetLnbVoltage(int descriptor, LnbVoltage voltage) =>
        Outcome(IoctlValue(descriptor, DvbIoctl.FrontendSetVoltage, (nint)(int)voltage));

    public unsafe SyscallOutcome SetPesFilter(int descriptor, byte[] filter)
    {
        fixed (byte* pointer = filter)
        {
            return Outcome(IoctlPointer(descriptor, DvbIoctl.DemuxSetPesFilter, pointer));
        }
    }

    public SyscallOutcome SetBufferSize(int descriptor, int bytes) =>
        Outcome(IoctlValue(descriptor, DvbIoctl.DemuxSetBufferSize, bytes));

    public SyscallOutcome StopFilter(int descriptor) =>
        Outcome(IoctlValue(descriptor, DvbIoctl.DemuxStop, 0));

    public unsafe SyscallOutcome ReadBytes(int descriptor, byte[] buffer, int count)
    {
        fixed (byte* pointer = buffer)
        {
            var read = ReadDescriptor(descriptor, pointer, (nuint)count);

            return read < 0
                ? SyscallOutcome.Failed(Marshal.GetLastPInvokeError())
                : SyscallOutcome.Ok((int)read);
        }
    }

    public unsafe SyscallOutcome WaitForReadable(int descriptor, int timeoutMilliseconds)
    {
        var waiting = new byte[DvbLayout.PollBytes];
        BinaryPrimitives.WriteInt32LittleEndian(
            waiting.AsSpan(DvbLayout.PollDescriptorAt),
            descriptor
        );
        BinaryPrimitives.WriteInt16LittleEndian(
            waiting.AsSpan(DvbLayout.PollEventsAt),
            ReadableEvent
        );

        fixed (byte* pointer = waiting)
        {
            return Outcome(PollDescriptors(pointer, OneDescriptor, timeoutMilliseconds));
        }
    }

    public void Rest(TimeSpan interval, CancellationToken cancellationToken) =>
        cancellationToken.WaitHandle.WaitOne(interval);

    private static unsafe SyscallOutcome WithPropertyHeader(
        int descriptor,
        uint request,
        byte[] records
    )
    {
        var header = new byte[DvbLayout.PropertyListHeaderBytes];

        fixed (byte* properties = records)
        fixed (byte* pointer = header)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                header.AsSpan(DvbLayout.PropertyListCountAt),
                (uint)(records.Length / DvbLayout.PropertyBytes)
            );
            BinaryPrimitives.WriteInt64LittleEndian(
                header.AsSpan(DvbLayout.PropertyListPointerAt),
                (long)properties
            );

            return Outcome(IoctlPointer(descriptor, request, pointer));
        }
    }

    private static SyscallOutcome Outcome(int result) =>
        result < 0
            ? SyscallOutcome.Failed(Marshal.GetLastPInvokeError())
            : SyscallOutcome.Ok(result);

    [LibraryImport(
        "libc",
        EntryPoint = "open",
        StringMarshalling = StringMarshalling.Utf8,
        SetLastError = true
    )]
    private static partial int OpenDescriptor(string path, int flags);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int CloseDescriptor(int descriptor);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static unsafe partial int IoctlPointer(int descriptor, nuint request, void* argument);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static partial int IoctlValue(int descriptor, nuint request, nint argument);

    [LibraryImport("libc", EntryPoint = "read", SetLastError = true)]
    private static unsafe partial nint ReadDescriptor(int descriptor, void* buffer, nuint count);

    [LibraryImport("libc", EntryPoint = "poll", SetLastError = true)]
    private static unsafe partial int PollDescriptors(
        void* descriptors,
        nuint count,
        int timeoutMilliseconds
    );
}
