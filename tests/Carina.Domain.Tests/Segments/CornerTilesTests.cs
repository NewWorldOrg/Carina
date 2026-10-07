using Carina.Domain.Encodings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class CornerTilesTests
{
    private const int CutWidth = WatermarkFrame.Width * 2;

    private const int CutHeight = WatermarkFrame.Height * 2;

    [Fact(DisplayName = "the tiles are the four corners of a frame shrunk to 960 by 540, 240 by 135 each, laid out two by two")]
    public void TheTilesAreTheFourCornersLaidOutTwoByTwo()
    {
        Assert.Equal((240, 135), (CornerTiles.TileWidth, CornerTiles.TileHeight));
        Assert.Equal((480, 270), (CornerTiles.Width, CornerTiles.Height));
        Assert.Equal(480 * 270, CornerTiles.Pixels);
    }

    [Fact(DisplayName = "the corners spread back from the tiles are the corners of the frame they were cut from, shrunk by half")]
    public void TheCornersSpreadBackAreTheCornersOfTheFrameShrunkByHalf()
    {
        byte[] cut = Noise(11);
        byte[] shrunk = Halved(cut);

        byte[] spread = Spread(Tiled(cut));

        Assert.All(
            Enumerable.Range(0, WatermarkFrame.Pixels).Where(WatermarkFrame.InACorner),
            pixel => Assert.True(spread[pixel] == shrunk[pixel], $"pixel {pixel % WatermarkFrame.Width},{pixel / WatermarkFrame.Width} is {spread[pixel]}, not {shrunk[pixel]}"));
    }

    [Fact(DisplayName = "a pixel just right of a left corner or just below a top corner repeats the edge of the corner, so no edge is read across where the tile was cut")]
    public void APixelJustBeyondACornerRepeatsItsEdge()
    {
        byte[] spread = Spread(Tiled(Noise(5)));
        int right = WatermarkFrame.CornerWidth;
        int below = WatermarkFrame.CornerHeight;

        Assert.All(
            Enumerable.Range(0, WatermarkFrame.CornerHeight).Concat(Enumerable.Range(WatermarkFrame.Height - WatermarkFrame.CornerHeight, WatermarkFrame.CornerHeight)),
            row => Assert.Equal(spread[WatermarkFrame.At(right - 1, row)], spread[WatermarkFrame.At(right, row)]));
        Assert.All(
            Enumerable.Range(0, WatermarkFrame.CornerWidth).Concat(Enumerable.Range(WatermarkFrame.Width - WatermarkFrame.CornerWidth, WatermarkFrame.CornerWidth)),
            column => Assert.Equal(spread[WatermarkFrame.At(column, below - 1)], spread[WatermarkFrame.At(column, below)]));
    }

    [Fact(DisplayName = "a mark in each corner of the frame the tiles were cut from gives the outline the frame shrunk by half gives")]
    public void AMarkInEachCornerGivesTheOutlineOfTheFrameShrunkByHalf()
    {
        byte[] cut = new byte[CutWidth * CutHeight];
        Array.Fill(cut, (byte)60);

        foreach ((int left, int top) in new[] { (40, 30), (CutWidth - 200, 20), (30, CutHeight - 110), (CutWidth - 170, CutHeight - 90) })
        {
            Box(cut, left, top, 120, 60);
        }

        byte[] expected = new byte[CornerOutline.Bytes];
        byte[] outline = new byte[CornerOutline.Bytes];
        CornerOutline.Draw(Halved(cut), expected);

        CornerOutline.Draw(Spread(Tiled(cut)), outline);

        Assert.Equal(expected, outline);
        Assert.Contains(outline, part => part is not 0);
    }

    [Fact(DisplayName = "tiles or a picture of the wrong size are refused")]
    public void TheWrongSizeIsRefused()
    {
        Assert.Throws<ArgumentException>(() => CornerTiles.Spread(new byte[CornerTiles.Pixels - 1], new byte[WatermarkFrame.Pixels]));
        Assert.Throws<ArgumentException>(() => CornerTiles.Spread(new byte[CornerTiles.Pixels], new byte[WatermarkFrame.Pixels + 1]));
    }

    private static byte[] Spread(byte[] tiles)
    {
        byte[] frame = new byte[WatermarkFrame.Pixels];

        CornerTiles.Spread(tiles, frame);

        return frame;
    }

    private static byte[] Noise(int seed)
    {
        byte[] frame = new byte[CutWidth * CutHeight];

        new Random(seed).NextBytes(frame);

        return frame;
    }

    private static void Box(byte[] frame, int left, int top, int width, int height)
    {
        for (int y = top; y < top + height; y++)
        {
            for (int x = left; x < left + width; x++)
            {
                frame[(y * CutWidth) + x] = 220;
            }
        }
    }

    private static byte[] Halved(byte[] cut)
    {
        byte[] shrunk = new byte[WatermarkFrame.Pixels];

        for (int y = 0; y < WatermarkFrame.Height; y++)
        {
            for (int x = 0; x < WatermarkFrame.Width; x++)
            {
                int at = (2 * y * CutWidth) + (2 * x);
                int sum = cut[at] + cut[at + 1] + cut[at + CutWidth] + cut[at + CutWidth + 1];

                shrunk[WatermarkFrame.At(x, y)] = (byte)((sum + 2) / 4);
            }
        }

        return shrunk;
    }

    private static byte[] Tiled(byte[] cut)
    {
        byte[] tiles = new byte[CornerTiles.Pixels];

        for (int corner = 0; corner < 4; corner++)
        {
            int fromLeft = (corner % 2) * (CutWidth - CornerTiles.TileWidth);
            int fromTop = (corner / 2) * (CutHeight - CornerTiles.TileHeight);
            int toLeft = (corner % 2) * CornerTiles.TileWidth;
            int toTop = (corner / 2) * CornerTiles.TileHeight;

            for (int y = 0; y < CornerTiles.TileHeight; y++)
            {
                cut.AsSpan(((fromTop + y) * CutWidth) + fromLeft, CornerTiles.TileWidth)
                    .CopyTo(tiles.AsSpan(((toTop + y) * CornerTiles.Width) + toLeft));
            }
        }

        return tiles;
    }
}
