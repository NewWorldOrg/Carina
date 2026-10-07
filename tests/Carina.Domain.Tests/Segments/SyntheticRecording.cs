using Carina.Domain.Encodings;
using Carina.Domain.Segments;
using Carina.Domain.Tests.Encodings;

namespace Carina.Domain.Tests.Segments;

internal sealed class SyntheticRecording
{
    private const int BarWidth = 8;

    public SyntheticRecording(int seconds, FrameClock clock)
    {
        Clock = clock;
        Sound = Noise(seconds * SoundReader.SampleRate);
        Frames = clock.FirstFrameFrom(TimeSpan.FromSeconds(seconds));
        Pictures = seconds;
    }

    public FrameClock Clock { get; }

    public short[] Sound { get; }

    public long Frames { get; }

    public int Pictures { get; }

    public long FrameBytes => Frames * FrameLight.Pixels;

    public long PictureBytes => (long)Pictures * WatermarkFrame.Pixels;

    public static void FrameBytesAt(long offset, Span<byte> into) => Copied(offset, into, FrameLight.Pixels, Frame);

    public static void PictureBytesAt(long offset, Span<byte> into) => Copied(offset, into, WatermarkFrame.Pixels, Picture);

    private static void Copied(long offset, Span<byte> into, int size, Func<long, byte[]> drawn)
    {
        for (int at = 0; at < into.Length;)
        {
            long which = (offset + at) / size;
            int inside = (int)((offset + at) % size);
            int length = Math.Min(size - inside, into.Length - at);
            drawn(which).AsSpan(inside, length).CopyTo(into[at..]);
            at += length;
        }
    }

    private static byte[] Frame(long frame)
    {
        byte[] drawn = new byte[FrameLight.Pixels];
        Array.Fill(drawn, (byte)(40 + (frame / 90 * 37 % 160)));
        int bar = (int)(frame % (FrameLight.Width - BarWidth));

        for (int y = 0; y < FrameLight.Height; y++)
        {
            Array.Fill<byte>(drawn, 220, (y * FrameLight.Width) + bar, BarWidth);
        }

        return drawn;
    }

    private static byte[] Picture(long picture)
    {
        byte[] drawn = WatermarkPictures.Filled((byte)(96 + (picture % 64)));
        int bar = (int)(picture * 37 % (WatermarkFrame.Width - BarWidth));

        for (int y = WatermarkFrame.Height / 2; y < WatermarkFrame.Height; y++)
        {
            Array.Fill<byte>(drawn, 16, (y * WatermarkFrame.Width) + bar, BarWidth);
        }

        return WatermarkPictures.Marked(drawn);
    }

    private static short[] Noise(int samples)
    {
        short[] sound = new short[2 * samples];
        uint state = 2463534242;

        for (int sample = 0; sample < samples; sample++)
        {
            int level = 500 + (sample / 1000 * 7919 % 6000);
            int shared = Next(ref state, level);
            sound[2 * sample] = (short)shared;
            sound[(2 * sample) + 1] = (short)((shared * 7 / 10) + Next(ref state, level / 3));
        }

        return sound;
    }

    private static int Next(ref uint state, int level)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;

        return (int)(state % (uint)(2 * level)) - level;
    }
}
