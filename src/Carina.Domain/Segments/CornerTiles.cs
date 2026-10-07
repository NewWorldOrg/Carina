using Carina.Domain.Encodings;

namespace Carina.Domain.Segments;

/// <summary>
/// A picture of the four corners of a frame shrunk to twice the size <see cref="WatermarkFrame"/> names,
/// each corner a quarter of its width and of its height, laid out two by two: top left, top right, bottom
/// left, bottom right, in grey, one byte a pixel, row after row. <see cref="Spread"/> puts each corner back
/// where it lies in the picture <see cref="WatermarkFrame"/> names, shrunk by half, every pixel the mean of
/// the two by two it covers. A pixel just right of a left corner or just below a top corner repeats the edge
/// of the corner, so no step is read across where the corner was cut; the rest of the picture is black.
/// </summary>
public static class CornerTiles
{
    public const int Shrink = 2;

    public const int CutFromWidth = WatermarkFrame.Width * Shrink;

    public const int CutFromHeight = WatermarkFrame.Height * Shrink;

    public const int TileWidth = CutFromWidth / 4;

    public const int TileHeight = CutFromHeight / 4;

    public const int Width = 2 * TileWidth;

    public const int Height = 2 * TileHeight;

    public const int Pixels = Width * Height;

    public static void Spread(ReadOnlySpan<byte> tiles, Span<byte> frame)
    {
        if (tiles.Length != Pixels)
        {
            throw new ArgumentException(
                $"The tiles of the corners are {Width} by {Height} in grey, {Pixels} bytes, and these are {tiles.Length}.",
                nameof(tiles));
        }

        WatermarkFrame.Sized(frame);

        frame.Clear();

        for (int corner = 0; corner < 4; corner++)
        {
            Place(tiles, frame, corner % 2, corner / 2);
        }
    }

    private static void Place(ReadOnlySpan<byte> tiles, Span<byte> frame, int right, int bottom)
    {
        int left = right * (WatermarkFrame.Width - WatermarkFrame.CornerWidth);
        int top = bottom * (WatermarkFrame.Height - WatermarkFrame.CornerHeight);
        int cutLeft = right * (CutFromWidth - TileWidth);
        int cutTop = bottom * (CutFromHeight - TileHeight);
        int tileLeft = right * TileWidth;
        int tileTop = bottom * TileHeight;

        for (int y = top; y < top + WatermarkFrame.CornerHeight; y++)
        {
            int row = tileTop + (Shrink * y) - cutTop;

            for (int x = left; x < left + WatermarkFrame.CornerWidth; x++)
            {
                frame[WatermarkFrame.At(x, y)] = Mean(tiles, (row * Width) + tileLeft + (Shrink * x) - cutLeft);
            }
        }

        RepeatTheEdges(frame, left, top, right is 0, bottom is 0);
    }

    private static void RepeatTheEdges(Span<byte> frame, int left, int top, bool onTheLeft, bool onTheTop)
    {
        int columnBeyond = left + WatermarkFrame.CornerWidth;
        int rowBeyond = top + WatermarkFrame.CornerHeight;

        for (int y = top; onTheLeft && y < rowBeyond; y++)
        {
            frame[WatermarkFrame.At(columnBeyond, y)] = frame[WatermarkFrame.At(columnBeyond - 1, y)];
        }

        for (int x = left; onTheTop && x < columnBeyond; x++)
        {
            frame[WatermarkFrame.At(x, rowBeyond)] = frame[WatermarkFrame.At(x, rowBeyond - 1)];
        }
    }

    private static byte Mean(ReadOnlySpan<byte> tiles, int at)
        => (byte)((tiles[at] + tiles[at + 1] + tiles[at + Width] + tiles[at + Width + 1] + 2) / 4);
}
