using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class CaptionCanvasTests
{
    private static readonly CaptionCanvas Small = new(new VideoSize(8, 4));

    [Fact]
    public void AFrameIsAsManyBytesAsTheCanvasHasPixelsTimesFour()
    {
        Assert.Equal(8 * 4 * 4, Small.FrameLength);
        Assert.Equal(1440 * 1080 * 4, new CaptionCanvas(new VideoSize(1440, 1080)).FrameLength);
    }

    [Fact]
    public void AFrameWithNothingOpaqueOnItIsNoCaption()
    {
        Assert.Null(Small.Drawn(new byte[Small.FrameLength]));
    }

    [Fact]
    public void WhatIsDrawnIsCutToTheSmallestBoxAroundEveryPixelThatIsNotFullyTransparent()
    {
        byte[] frame = new byte[Small.FrameLength];

        Paint(frame, 2, 1, 0xff);
        Paint(frame, 5, 1, 0x01);
        Paint(frame, 3, 2, 0xff);

        CaptionPicture? drawn = Small.Drawn(frame);

        Assert.NotNull(drawn);
        Assert.Equal((2, 1, 4, 2), (drawn.Left, drawn.Top, drawn.Width, drawn.Height));

        PalettePngTests.Decoded png = PalettePngTests.Decoded.Of(drawn.Png.ToArray());

        Assert.Equal(4, png.Width);
        Assert.Equal(2, png.Height);
        Assert.Equal(3, png.ColourType);
        Assert.Equal([0xff, 0, 0, 0x01, 0, 0xff, 0, 0], png.Pixels().Select(pixel => (int)pixel.Alpha).ToArray());
    }

    [Fact]
    public void ASinglePixelIsABoxOfOne()
    {
        byte[] frame = new byte[Small.FrameLength];

        Paint(frame, 7, 3, 0xff);

        CaptionPicture? drawn = Small.Drawn(frame);

        Assert.NotNull(drawn);
        Assert.Equal((7, 3, 1, 1), (drawn.Left, drawn.Top, drawn.Width, drawn.Height));
    }

    [Fact]
    public void AFrameOfAnotherLengthIsRefused()
    {
        Assert.Throws<ArgumentException>(() => Small.Drawn(new byte[Small.FrameLength - 1]));
    }

    [Fact]
    public void ACanvasTheWireCannotMeasureIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CaptionCanvas(new VideoSize(65_536, 1)));
    }

    private static void Paint(byte[] frame, int column, int row, byte alpha)
    {
        int at = ((row * 8) + column) * 4;

        frame[at] = 0x10;
        frame[at + 1] = 0x20;
        frame[at + 2] = 0x30;
        frame[at + 3] = alpha;
    }
}
