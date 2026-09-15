namespace Carina.Domain.Encodings;

/// <summary>
/// The picture a station's watermark is looked for in: a frame shrunk to one fixed size and taken
/// in grey, one byte a pixel, row after row. Only the four corners are looked at, because that is
/// where a station puts its mark. What is looked at there is an edge — a step in brightness to the
/// pixel across or below — rather than the brightness itself, because a mark laid half-transparent
/// over a moving picture keeps its outline while its shade moves with whatever is behind it.
/// </summary>
public static class WatermarkFrame
{
    public const int Width = 480;

    public const int Height = 270;

    public const int Pixels = Width * Height;

    public const int CornerWidth = Width / 4;

    public const int CornerHeight = Height / 4;

    public const int CornerPixels = 4 * CornerWidth * CornerHeight;

    public const int EdgeStep = 24;

    private static readonly int[] InTheCorners = [.. Enumerable.Range(0, Pixels).Where(InACorner)];

    public static ReadOnlySpan<int> Corners => InTheCorners;

    public static bool InACorner(int pixel)
    {
        if (pixel is < 0 or >= Pixels)
        {
            return false;
        }

        int x = pixel % Width;
        int y = pixel / Width;

        return (x < CornerWidth || x >= Width - CornerWidth) && (y < CornerHeight || y >= Height - CornerHeight);
    }

    public static int At(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);

        return (y * Width) + x;
    }

    public static bool IsEdge(ReadOnlySpan<byte> frame, int pixel)
    {
        int here = frame[pixel];
        int across = pixel % Width < Width - 1 ? Math.Abs(frame[pixel + 1] - here) : 0;
        int below = pixel / Width < Height - 1 ? Math.Abs(frame[pixel + Width] - here) : 0;

        return across + below >= EdgeStep;
    }

    public static void Sized(ReadOnlySpan<byte> frame)
    {
        if (frame.Length != Pixels)
        {
            throw new ArgumentException(
                $"A picture looked in for a watermark is {Width} by {Height} in grey, {Pixels} bytes, and this one is {frame.Length}.",
                nameof(frame));
        }
    }
}
