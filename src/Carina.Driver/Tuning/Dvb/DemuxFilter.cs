using System.Buffers.Binary;

namespace Carina.Driver.Tuning.Dvb;

public static class DemuxFilter
{
    public const ushort EveryPid = 0x2000;

    private const uint FromTheFrontend = 0;
    private const uint IntoTheTransportStreamTap = 2;
    private const uint WithoutInterpretingThePayload = 20;
    private const uint StartingImmediately = 4;

    public static byte[] EverythingFromTheFrontend()
    {
        byte[] filter = new byte[DvbLayout.PesFilterBytes];

        BinaryPrimitives.WriteUInt16LittleEndian(
            filter.AsSpan(DvbLayout.PesFilterPidAt),
            EveryPid
        );
        BinaryPrimitives.WriteUInt32LittleEndian(
            filter.AsSpan(DvbLayout.PesFilterInputAt),
            FromTheFrontend
        );
        BinaryPrimitives.WriteUInt32LittleEndian(
            filter.AsSpan(DvbLayout.PesFilterOutputAt),
            IntoTheTransportStreamTap
        );
        BinaryPrimitives.WriteUInt32LittleEndian(
            filter.AsSpan(DvbLayout.PesFilterPesTypeAt),
            WithoutInterpretingThePayload
        );
        BinaryPrimitives.WriteUInt32LittleEndian(
            filter.AsSpan(DvbLayout.PesFilterFlagsAt),
            StartingImmediately
        );

        return filter;
    }
}
