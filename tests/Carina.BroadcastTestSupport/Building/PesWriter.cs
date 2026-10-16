namespace Carina.BroadcastTestSupport;

public static class PesWriter
{
    public const int PrivateStream1 = 0xBD;

    public const int FirstAudioStream = 0xC0;

    public const int HeaderLength = 9;

    public const int TimestampLength = 5;

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

    /// <summary>
    /// A PES of the first audio stream, stamped with <paramref name="pts"/> when there is one to stamp.
    /// </summary>
    public static byte[] Sound(long? pts, ReadOnlySpan<byte> data)
    {
        ByteWriter writer = new ByteWriter()
            .Byte(0x00)
            .Byte(0x00)
            .Byte(0x01)
            .Byte(FirstAudioStream)
            .Word(HeaderLength - 6 + (pts is null ? 0 : TimestampLength) + data.Length)
            .Byte(0x80)
            .Byte(pts is null ? 0x00 : 0x80)
            .Byte(pts is null ? 0 : TimestampLength);

        if (pts is { } stamped)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(stamped);
            writer.Run(Timestamp(stamped));
        }

        return writer.Run(data).ToArray();
    }

    public static long TimestampOf(ReadOnlySpan<byte> pes)
        => ((long)(pes[HeaderLength] >> 1) & 0x07) << 30
           | (long)pes[HeaderLength + 1] << 22
           | (long)(pes[HeaderLength + 2] >> 1) << 15
           | (long)pes[HeaderLength + 3] << 7
           | (long)(pes[HeaderLength + 4] >> 1);

    public static byte[] Packets(int pid, ReadOnlySpan<byte> pes)
    {
        var writer = new TransportStreamWriter(pid);

        Packets(writer, pes);

        return writer.Bytes;
    }

    /// <summary>
    /// Carries <paramref name="pes"/> on the packets <paramref name="writer"/> writes next, so that the counter
    /// runs on unbroken from whatever it carried before.
    /// </summary>
    public static void Packets(TransportStreamWriter writer, ReadOnlySpan<byte> pes)
    {
        ArgumentNullException.ThrowIfNull(writer);

        int written = 0;

        while (written < pes.Length)
        {
            int take = Math.Min(TransportStreamWriter.PayloadCapacity, pes.Length - written);
            int stuffing = TransportStreamWriter.PayloadCapacity - take;

            writer.Packet(null, pes.Slice(written, take), stuffing - 1, unitStart: written is 0);
            written += take;
        }
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
