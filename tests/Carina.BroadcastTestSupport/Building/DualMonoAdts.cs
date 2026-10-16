namespace Carina.BroadcastTestSupport;

public static class DualMonoAdts
{
    public const int SampleRate = 48_000;

    public const int SamplesPerFrame = 1_024;

    public const int HeaderLength = 7;

    public const int Stereo = 2;

    private const int LowComplexity = 1;

    private const int FortyEightKilohertz = 3;

    private const int ChannelsTheFrameDescribes = 0;

    public static byte[] Silence(TimeSpan length)
    {
        byte[] frame = SilentFrame();
        int frames = (int)Math.Ceiling(length.TotalSeconds * SampleRate / SamplesPerFrame);

        return [.. Enumerable.Repeat(frame, frames).SelectMany(held => held)];
    }

    public static byte[] SilentFrame()
    {
        byte[] block = RawDataBlock();

        return [.. Header(HeaderLength + block.Length, ChannelsTheFrameDescribes), .. block];
    }

    /// <summary>
    /// The header of a low complexity frame at 48 kHz, <paramref name="frameLength"/> bytes long with the header
    /// counted, carrying the channels <paramref name="channelConfiguration"/> names.
    /// </summary>
    public static byte[] Header(int frameLength, int channelConfiguration)
        => new BitWriter()
            .Bits(0xFFF, 12)
            .Bits(0, 1)
            .Bits(0, 2)
            .Bits(1, 1)
            .Bits(LowComplexity, 2)
            .Bits(FortyEightKilohertz, 4)
            .Bits(0, 1)
            .Bits(channelConfiguration, 3)
            .Bits(0, 1)
            .Bits(0, 1)
            .Bits(0, 1)
            .Bits(0, 1)
            .Bits(frameLength, 13)
            .Bits(0x7FF, 11)
            .Bits(0, 2)
            .ToArray();

    public static int FrameLengthOf(ReadOnlySpan<byte> header)
        => ((header[3] & 0x03) << 11) | (header[4] << 3) | (header[5] >> 5);

    private static byte[] RawDataBlock()
    {
        BitWriter bits = new BitWriter()
            .Bits(5, 3)
            .Bits(0, 4)
            .Bits(LowComplexity, 2)
            .Bits(FortyEightKilohertz, 4)
            .Bits(2, 4)
            .Bits(0, 4)
            .Bits(0, 4)
            .Bits(0, 2)
            .Bits(0, 3)
            .Bits(0, 4)
            .Bits(0, 1)
            .Bits(0, 1)
            .Bits(0, 1)
            .Bits(0, 1)
            .Bits(0, 4)
            .Bits(0, 1)
            .Bits(1, 4)
            .Align()
            .Bits(0, 8);

        for (int channel = 0; channel < 2; channel++)
        {
            bits.Bits(0, 3)
                .Bits(channel, 4)
                .Bits(100, 8)
                .Bits(0, 1)
                .Bits(0, 2)
                .Bits(0, 1)
                .Bits(0, 6)
                .Bits(0, 1)
                .Bits(0, 1)
                .Bits(0, 1)
                .Bits(0, 1);
        }

        return bits.Bits(7, 3).ToArray();
    }
}
