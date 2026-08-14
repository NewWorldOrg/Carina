namespace Carina.Driver.Tuning.Dvb;

public static class DvbIoctl
{
    private const int NumberBits = 8;
    private const int TypeBits = 8;
    private const int SizeBits = 14;

    private const int NumberShift = 0;
    private const int TypeShift = NumberShift + NumberBits;
    private const int SizeShift = TypeShift + TypeBits;
    private const int DirectionShift = SizeShift + SizeBits;

    private const uint NoTransfer = 0;
    private const uint WritesToTheDriver = 1;
    private const uint ReadsFromTheDriver = 2;

    private const uint DvbType = 'o';

    private const uint FrontendGetInfoNumber = 61;
    private const uint FrontendSetVoltageNumber = 67;
    private const uint FrontendReadStatusNumber = 69;
    private const uint FrontendSetPropertyNumber = 82;
    private const uint FrontendGetPropertyNumber = 83;

    private const uint DemuxStartNumber = 41;
    private const uint DemuxStopNumber = 42;
    private const uint DemuxSetPesFilterNumber = 44;
    private const uint DemuxSetBufferSizeNumber = 45;

    public static readonly uint FrontendGetInfo = Reading(
        FrontendGetInfoNumber,
        DvbLayout.FrontendInfoBytes
    );

    public static readonly uint FrontendSetVoltage = ByValue(FrontendSetVoltageNumber);

    public static readonly uint FrontendReadStatus = Reading(
        FrontendReadStatusNumber,
        DvbLayout.FrontendStatusBytes
    );

    public static readonly uint FrontendSetProperty = Writing(
        FrontendSetPropertyNumber,
        DvbLayout.PropertyListHeaderBytes
    );

    public static readonly uint FrontendGetProperty = Reading(
        FrontendGetPropertyNumber,
        DvbLayout.PropertyListHeaderBytes
    );

    public static readonly uint DemuxStart = ByValue(DemuxStartNumber);

    public static readonly uint DemuxStop = ByValue(DemuxStopNumber);

    public static readonly uint DemuxSetBufferSize = ByValue(DemuxSetBufferSizeNumber);

    public static readonly uint DemuxSetPesFilter = Writing(
        DemuxSetPesFilterNumber,
        DvbLayout.PesFilterBytes
    );

    private static uint ByValue(uint number) => Encode(NoTransfer, number, 0);

    private static uint Reading(uint number, int payloadBytes) =>
        Encode(ReadsFromTheDriver, number, payloadBytes);

    private static uint Writing(uint number, int payloadBytes) =>
        Encode(WritesToTheDriver, number, payloadBytes);

    private static uint Encode(uint direction, uint number, int payloadBytes) =>
        (direction << DirectionShift)
        | ((uint)payloadBytes << SizeShift)
        | (DvbType << TypeShift)
        | (number << NumberShift);
}
