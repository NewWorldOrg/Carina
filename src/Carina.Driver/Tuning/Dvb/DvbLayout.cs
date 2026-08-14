using System.Runtime.InteropServices;

namespace Carina.Driver.Tuning.Dvb;

public static class DvbLayout
{
    private const int ByteBytes = 1;
    private const int HalfWordBytes = 2;
    private const int WordBytes = 4;
    private const int LongWordBytes = 8;
    private const int PointerBytes = 8;
    private const int EnumBytes = WordBytes;

    private const int PropertyReservedWords = 3;
    private const int BufferReservedWords = 3;

    public const int PropertyCommandAt = 0;
    private const int PropertyReservedAt = PropertyCommandAt + WordBytes;
    public const int PropertyPayloadAt =
        PropertyReservedAt + (PropertyReservedWords * WordBytes);

    public const int StatisticCountAt = PropertyPayloadAt;
    private const int StatisticCountBytes = ByteBytes;
    public const int StatisticsAt = StatisticCountAt + StatisticCountBytes;
    public const int StatisticScaleBytes = ByteBytes;
    private const int StatisticValueBytes = LongWordBytes;
    public const int StatisticBytes = StatisticScaleBytes + StatisticValueBytes;
    public const int MaxStatisticLayers = 4;

    public const int BufferDataAt = PropertyPayloadAt;
    public const int BufferDataBytes = 32;
    public const int BufferLengthAt = BufferDataAt + BufferDataBytes;
    private const int BufferReservedAt = BufferLengthAt + WordBytes;
    private const int BufferPointerAt = BufferReservedAt + (BufferReservedWords * WordBytes);
    private const int BufferEndsAt = BufferPointerAt + PointerBytes;

    public const int PayloadBytes = BufferEndsAt - PropertyPayloadAt;
    public const int PropertyResultAt = PropertyPayloadAt + PayloadBytes;
    public const int PropertyBytes = PropertyResultAt + WordBytes;

    private const int PropertyListPointerAlignmentBytes = WordBytes;
    public const int PropertyListCountAt = 0;
    public const int PropertyListPointerAt =
        PropertyListCountAt + WordBytes + PropertyListPointerAlignmentBytes;
    public const int PropertyListHeaderBytes = PropertyListPointerAt + PointerBytes;

    public const int FrontendStatusBytes = EnumBytes;

    public const int FrontendNameAt = 0;
    public const int FrontendNameBytes = 128;
    private const int FrontendRangeWords = 8;
    public const int FrontendInfoBytes =
        FrontendNameBytes + EnumBytes + (FrontendRangeWords * WordBytes) + EnumBytes;

    private const int PesFilterPidBytes = HalfWordBytes;
    private const int PesFilterPidPaddingBytes = HalfWordBytes;
    public const int PesFilterPidAt = 0;
    public const int PesFilterInputAt = PesFilterPidAt + PesFilterPidBytes + PesFilterPidPaddingBytes;
    public const int PesFilterOutputAt = PesFilterInputAt + EnumBytes;
    public const int PesFilterPesTypeAt = PesFilterOutputAt + EnumBytes;
    public const int PesFilterFlagsAt = PesFilterPesTypeAt + EnumBytes;
    public const int PesFilterBytes = PesFilterFlagsAt + WordBytes;

    public const int PollDescriptorAt = 0;
    public const int PollEventsAt = PollDescriptorAt + WordBytes;
    public const int PollReturnedEventsAt = PollEventsAt + HalfWordBytes;
    public const int PollBytes = PollReturnedEventsAt + HalfWordBytes;

    public static bool DescribesThisMachine =>
        BitConverter.IsLittleEndian
        && RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64;
}
