namespace Carina.BroadcastTestSupport;

public static class PesWriter
{
    public const int PrivateStream1 = 0xBD;

    public const int HeaderLength = 9;

    public static byte[] PrivateStream(long pts, ReadOnlySpan<byte> data)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pts);

        return new ByteWriter()
            .Byte(0x00)
            .Byte(0x00)
            .Byte(0x01)
            .Byte(PrivateStream1)
            .Word(HeaderLength - 6 + 5 + data.Length)
            .Byte(0x84)
            .Byte(0x80)
            .Byte(0x05)
            .Run(Timestamp(pts))
            .Run(data)
            .ToArray();
    }

    public static byte[] Packets(int pid, ReadOnlySpan<byte> pes)
    {
        var writer = new TransportStreamWriter(pid);
        int written = 0;

        while (written < pes.Length)
        {
            int take = Math.Min(TransportStreamWriter.PayloadCapacity, pes.Length - written);
            int stuffing = TransportStreamWriter.PayloadCapacity - take;

            writer.Packet(null, pes.Slice(written, take), stuffing - 1, unitStart: written is 0);
            written += take;
        }

        return writer.Bytes;
    }

    private static byte[] Timestamp(long pts)
        =>
        [
            (byte)(0x21 | ((pts >> 29) & 0x0E)),
            (byte)((pts >> 22) & 0xFF),
            (byte)(0x01 | ((pts >> 14) & 0xFE)),
            (byte)((pts >> 7) & 0xFF),
            (byte)(0x01 | ((pts << 1) & 0xFE)),
        ];
}
