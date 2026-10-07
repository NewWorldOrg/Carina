using Carina.Domain.Encodings;
using Carina.Domain.Segments;
using Carina.Domain.Tests.Encodings;

namespace Carina.Domain.Tests.Segments;

public sealed class CornerOutlineTests
{
    private const int TopRight = 1;

    [Fact(DisplayName = "an outline is 30 by 17 blocks in each of four corners, 255 bytes a picture whatever the picture holds")]
    public void AnOutlineIsOfOneFixedSize()
    {
        Assert.Equal(30, CornerOutline.Columns);
        Assert.Equal(17, CornerOutline.Rows);
        Assert.Equal(2040, CornerOutline.Bits);
        Assert.Equal(255, CornerOutline.Bytes);

        CornerReader reader = new();
        List<byte> outlines = [];
        reader.Glimpse(WatermarkPictures.Plain(), outlines);
        reader.Glimpse(WatermarkPictures.Moving(3, marked: true), outlines);
        reader.Glimpse(Noise(9), outlines);

        Assert.Equal(3 * CornerOutline.Bytes, outlines.Count);
        Assert.Equal(3, reader.Pictures);
    }

    [Fact(DisplayName = "a plain picture has no outline")]
    public void APlainPictureHasNoOutline()
        => Assert.All(Drawn(WatermarkPictures.Plain()), part => Assert.Equal(0, part));

    [Fact(DisplayName = "a mark outlined in a corner keeps its shape: the blocks its outline crosses are on and the blocks inside it are off")]
    public void AMarkKeepsItsShape()
    {
        byte[] outline = Drawn(WatermarkPictures.Marked(WatermarkPictures.Plain()));
        int left = Column(WatermarkPictures.MarkLeft - 1);
        int right = Column(WatermarkPictures.MarkRight);
        int top = WatermarkPictures.MarkTop / CornerOutline.Block;
        int bottom = WatermarkPictures.MarkBottom / CornerOutline.Block;

        Assert.All(Enumerable.Range(left, right - left + 1), column =>
        {
            Assert.True(CornerOutline.IsOn(outline, TopRight, column, top));
            Assert.True(CornerOutline.IsOn(outline, TopRight, column, bottom));
        });
        Assert.All(Enumerable.Range(top, bottom - top + 1), row =>
        {
            Assert.True(CornerOutline.IsOn(outline, TopRight, left, row));
            Assert.True(CornerOutline.IsOn(outline, TopRight, right, row));
        });
        Assert.All(Enumerable.Range(left + 2, right - left - 3), column =>
            Assert.All(Enumerable.Range(top + 1, bottom - top - 1), row => Assert.False(CornerOutline.IsOn(outline, TopRight, column, row))));
        Assert.Equal(OnBlocks(outline, TopRight), OnBlocks(outline, TopRight).Where(block => InTheMark(block, left, right, top, bottom)));
        Assert.All(new[] { 0, 2, 3 }, corner => Assert.Empty(OnBlocks(outline, corner)));
    }

    [Fact(DisplayName = "a mark outside the corners leaves the outline empty")]
    public void AMarkOutsideTheCornersIsNotDrawn()
        => Assert.All(Drawn(WatermarkPictures.Outlined(WatermarkPictures.Plain(), 200, 120, 280, 150)), part => Assert.Equal(0, part));

    [Fact(DisplayName = "a dot whose three edges fall one to a block leaves every block off, and the same dot inside one block turns it on")]
    public void ABlockNeedsTwoEdges()
    {
        byte[] onTheCorner = WatermarkPictures.Plain();
        onTheCorner[WatermarkFrame.At(4, 4)] = WatermarkPictures.Backdrop + 30;
        byte[] inside = WatermarkPictures.Plain();
        inside[WatermarkFrame.At(6, 6)] = WatermarkPictures.Backdrop + 30;

        Assert.All(Drawn(onTheCorner), part => Assert.Equal(0, part));
        Assert.Equal((1, 1), Assert.Single(OnBlocks(Drawn(inside), 0)));
    }

    [Fact(DisplayName = "a picture of any other size, or room for an outline of any other size, is refused")]
    public void OtherSizesAreRefused()
    {
        Assert.Throws<ArgumentException>(() => CornerOutline.Draw(new byte[WatermarkFrame.Pixels - 1], new byte[CornerOutline.Bytes]));
        Assert.Throws<ArgumentException>(() => CornerOutline.Draw(new byte[WatermarkFrame.Pixels], new byte[CornerOutline.Bytes + 1]));
    }

    [Fact(DisplayName = "pictures handed over in pieces of any length draw the same outlines as handed over whole")]
    public void PiecesDrawTheSameAsWhole()
    {
        byte[] pictures = [.. Enumerable.Range(0, 6).SelectMany(step => WatermarkPictures.Moving(step, marked: step % 2 is 0))];
        CornerReader whole = new();
        List<byte> wholeOutlines = [];
        whole.Glimpse(pictures, wholeOutlines);
        CornerReader pieced = new();
        List<byte> piecedOutlines = [];
        Random random = new(8);

        for (int at = 0; at < pictures.Length;)
        {
            int length = Math.Min(pictures.Length - at, random.Next(1, 2 * WatermarkFrame.Pixels));
            pieced.Glimpse(pictures.AsSpan(at, length), piecedOutlines);
            at += length;
        }

        Assert.Equal(wholeOutlines, piecedOutlines);
    }

    private static byte[] Drawn(byte[] picture)
    {
        byte[] outline = new byte[CornerOutline.Bytes];
        CornerOutline.Draw(picture, outline);

        return outline;
    }

    private static int Column(int x) => (x - (WatermarkFrame.Width - WatermarkFrame.CornerWidth)) / CornerOutline.Block;

    private static bool InTheMark((int Column, int Row) block, int left, int right, int top, int bottom)
        => block.Column >= left && block.Column <= right && block.Row >= top && block.Row <= bottom;

    private static List<(int Column, int Row)> OnBlocks(byte[] outline, int corner)
        => [.. Enumerable.Range(0, CornerOutline.BlocksInACorner)
            .Select(block => (Column: block % CornerOutline.Columns, Row: block / CornerOutline.Columns))
            .Where(block => CornerOutline.IsOn(outline, corner, block.Column, block.Row))];

    private static byte[] Noise(int seed)
    {
        Random random = new(seed);

        return [.. Enumerable.Range(0, WatermarkFrame.Pixels).Select(_ => (byte)random.Next(0, 256))];
    }
}
