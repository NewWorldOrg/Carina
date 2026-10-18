using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

/// <summary>
/// Corner outlines, one a second, made up for the tests: a logo drawn as a block rectangle where the
/// second shows it, over blocks of noise drawn from a fixed seed.
/// </summary>
internal static class SyntheticCorners
{
    public const double Noise = 0.04;

    public static readonly int[] TopRight = Rectangle(1, 24, 1, 4, 3);

    public static readonly int[] TopLeft = Rectangle(0, 2, 1, 5, 3);

    public static int[] Rectangle(int corner, int column, int row, int columns, int rows)
        => [.. Enumerable.Range(0, rows).SelectMany(down => Enumerable.Range(0, columns)
            .Select(across => (corner * CornerOutline.BlocksInACorner) + ((row + down) * CornerOutline.Columns) + column + across))];

    public static byte[] Drawn(int seconds, int[] logo, Func<int, bool> shows, int seed)
    {
        Random noise = new(seed);
        byte[] outlines = new byte[seconds * CornerOutline.Bytes];

        for (int second = 0; second < seconds; second++)
        {
            Span<byte> outline = outlines.AsSpan(second * CornerOutline.Bytes, CornerOutline.Bytes);

            for (int block = 0; block < CornerOutline.Bits; block++)
            {
                if (noise.NextDouble() < Noise)
                {
                    On(outline, block);
                }
            }

            if (shows(second))
            {
                foreach (int block in logo)
                {
                    On(outline, block);
                }
            }
        }

        return outlines;
    }

    private static void On(Span<byte> outline, int block) => outline[block >> 3] |= (byte)(1 << (block & 7));
}
