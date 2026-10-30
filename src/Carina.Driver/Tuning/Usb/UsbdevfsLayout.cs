using System.Runtime.InteropServices;

namespace Carina.Driver.Tuning.Usb;

/// <summary>
/// The usbdevfs structures and requests from linux/usbdevice_fs.h, as laid out on a 64 bit little endian machine.
/// </summary>
public static class UsbdevfsLayout
{
    private const uint NoData = 0;
    private const uint Writes = 1;
    private const uint Reads = 2;
    private const char Magic = 'U';

    public const int ControlRequestTypeAt = 0;
    public const int ControlRequestAt = 1;
    public const int ControlValueAt = 2;
    public const int ControlIndexAt = 4;
    public const int ControlLengthAt = 6;
    public const int ControlTimeoutAt = 8;
    public const int ControlDataAt = 16;
    public const int ControlBytes = 24;

    public const int UrbTypeAt = 0;
    public const int UrbEndpointAt = 1;
    public const int UrbStatusAt = 4;
    public const int UrbBufferAt = 16;
    public const int UrbBufferLengthAt = 24;
    public const int UrbActualLengthAt = 28;
    public const int UrbUserContextAt = 48;
    public const int UrbBytes = 56;

    public const byte BulkUrb = 3;

    public const int DescriptorBytes = 18;
    public const int DescriptorVendorAt = 8;
    public const int DescriptorProductAt = 10;

    public static readonly uint Control = Request(Reads | Writes, 0, ControlBytes);

    public static readonly uint SubmitUrb = Request(Reads, 10, UrbBytes);

    public static readonly uint DiscardUrb = Request(NoData, 11, 0);

    public static readonly uint ReapUrbWithoutWaiting = Request(Writes, 13, sizeof(long));

    public static readonly uint ClaimInterface = Request(Reads, 15, sizeof(uint));

    public static readonly uint ReleaseInterface = Request(Reads, 16, sizeof(uint));

    public static bool DescribesThisMachine =>
        BitConverter.IsLittleEndian && RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64;

    private static uint Request(uint direction, uint number, int size) =>
        (direction << 30) | ((uint)size << 16) | ((uint)Magic << 8) | number;
}
