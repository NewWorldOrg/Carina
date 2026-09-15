using Carina.Domain.Encodings;

namespace Carina.Domain.Tests.Encodings;

internal static class WatermarkPictures
{
    public const byte Backdrop = 128;

    public const int MarkLeft = 400;

    public const int MarkTop = 10;

    public const int MarkRight = 440;

    public const int MarkBottom = 30;

    public static byte[] Plain() => Filled(Backdrop);

    public static byte[] Filled(byte shade)
    {
        byte[] frame = new byte[WatermarkFrame.Pixels];
        Array.Fill(frame, shade);

        return frame;
    }

    public static byte[] Moving(int step, bool marked)
    {
        byte[] frame = Filled((byte)(96 + (step % 64)));
        int bar = (step * 37) % (WatermarkFrame.Width - 8);

        for (int y = WatermarkFrame.Height / 2; y < WatermarkFrame.Height; y++)
        {
            for (int x = bar; x < bar + 8; x++)
            {
                frame[(y * WatermarkFrame.Width) + x] = 16;
            }
        }

        return marked ? Marked(frame) : frame;
    }

    public static byte[] Marked(byte[] frame) => Outlined(frame, MarkLeft, MarkTop, MarkRight, MarkBottom);

    public static byte[] Outlined(byte[] frame, int left, int top, int right, int bottom)
    {
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                bool border = x <= left + 1 || x >= right - 1 || y <= top + 1 || y >= bottom - 1;

                if (border)
                {
                    frame[(y * WatermarkFrame.Width) + x] = 255;
                }
            }
        }

        return frame;
    }

    public static WatermarkMask Learned(int frames = WatermarkLearner.FewestFrames)
    {
        var learner = new WatermarkLearner();

        for (int step = 0; step < frames; step++)
        {
            learner.Pictured(Moving(step, marked: true));
        }

        return learner.Learned() ?? throw new InvalidOperationException("The mark drawn in the corner was not learned.");
    }
}
