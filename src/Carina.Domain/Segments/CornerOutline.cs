using Carina.Domain.Encodings;

namespace Carina.Domain.Segments;

/// <summary>
/// The outline in the four corners of a picture looked in for a station's watermark, the corners
/// and the edges <see cref="WatermarkFrame"/> names, shrunk to blocks of <see cref="Block"/> by
/// <see cref="Block"/> pixels: a block is on when at least <see cref="EdgesInABlock"/> of its pixels
/// are an edge. A corner is <see cref="Columns"/> by <see cref="Rows"/> blocks from its top left,
/// the last row shorter. The corners follow one another top left, top right, bottom left, bottom
/// right, each row after row, one bit a block, the lowest bit of a byte first.
/// </summary>
public static class CornerOutline
{
    public const int Block = 4;

    public const int Corners = 4;

    public const int Columns = WatermarkFrame.CornerWidth / Block;

    public const int Rows = (WatermarkFrame.CornerHeight + Block - 1) / Block;

    public const int BlocksInACorner = Columns * Rows;

    public const int Bits = Corners * BlocksInACorner;

    public const int Bytes = (Bits + 7) / 8;

    public const int EdgesInABlock = 2;

    public const int PerChunk = LearningData.ChunkSeconds;

    public static void Draw(ReadOnlySpan<byte> frame, Span<byte> outline)
    {
        WatermarkFrame.Sized(frame);
        Sized(outline.Length);

        outline.Clear();

        for (int block = 0; block < Bits; block++)
        {
            if (Edged(frame, block))
            {
                outline[block >> 3] |= (byte)(1 << (block & 7));
            }
        }
    }

    public static bool IsOn(ReadOnlySpan<byte> outline, int corner, int column, int row)
    {
        Sized(outline.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(corner);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(corner, Corners);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, Columns);
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, Rows);

        int block = (corner * BlocksInACorner) + (row * Columns) + column;

        return (outline[block >> 3] & (1 << (block & 7))) is not 0;
    }

    private static bool Edged(ReadOnlySpan<byte> frame, int block)
    {
        int corner = block / BlocksInACorner;
        int row = block % BlocksInACorner / Columns;
        int column = block % Columns;
        int left = ((corner % 2) * (WatermarkFrame.Width - WatermarkFrame.CornerWidth)) + (column * Block);
        int cornerTop = (corner / 2) * (WatermarkFrame.Height - WatermarkFrame.CornerHeight);
        int top = cornerTop + (row * Block);
        int bottom = Math.Min(top + Block, cornerTop + WatermarkFrame.CornerHeight);
        int edges = 0;

        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < left + Block; x++)
            {
                if (WatermarkFrame.IsEdge(frame, WatermarkFrame.At(x, y)) && ++edges >= EdgesInABlock)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void Sized(int length)
    {
        if (length != Bytes)
        {
            throw new ArgumentException($"A corner outline is {Bytes} bytes, and this one is {length}.");
        }
    }
}
