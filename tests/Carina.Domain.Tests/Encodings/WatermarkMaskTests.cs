using Carina.Domain.Encodings;

namespace Carina.Domain.Tests.Encodings;

public sealed class WatermarkMaskTests
{
    [Fact(DisplayName = "a learned mark is seen in a picture that carries it over something else moving, and not in one without it")]
    public void ALearnedMarkIsSeenWhereItIsOnScreenAndNotWhereItIsNot()
    {
        WatermarkMask learned = WatermarkPictures.Learned();

        Assert.True(learned.SeenIn(WatermarkPictures.Moving(WatermarkLearner.FewestFrames + 11, marked: true)));
        Assert.False(learned.SeenIn(WatermarkPictures.Moving(WatermarkLearner.FewestFrames + 11, marked: false)));
        Assert.False(learned.SeenIn(WatermarkPictures.Plain()));
    }

    [Fact(DisplayName = "a mark with less than half of its outline showing is not seen")]
    public void AMarkWithLessThanHalfOfItsOutlineShowingIsNotSeen()
    {
        WatermarkMask learned = WatermarkPictures.Learned();
        byte[] corner = WatermarkPictures.Outlined(
            WatermarkPictures.Plain(),
            WatermarkPictures.MarkLeft,
            WatermarkPictures.MarkTop,
            WatermarkPictures.MarkLeft + 4,
            WatermarkPictures.MarkTop + 4);

        Assert.False(learned.SeenIn(corner));
    }

    [Fact(DisplayName = "a mark written down is read back as the same mark")]
    public void AMarkWrittenDownIsReadBackAsTheSameMark()
    {
        WatermarkMask learned = WatermarkPictures.Learned();

        byte[] kept = learned.Packed();
        WatermarkMask read = WatermarkMask.Unpacked(kept);

        Assert.Equal(WatermarkMask.PackedBytes, kept.Length);
        Assert.Equal(kept, read.Packed());
        Assert.Equal(learned.Pixels, read.Pixels);
        Assert.True(read.Covers(WatermarkPictures.MarkLeft - 1, 20));
    }

    [Fact(DisplayName = "what is handed out is a copy, so nothing outside can change the mark")]
    public void WhatIsHandedOutIsACopy()
    {
        WatermarkMask learned = WatermarkPictures.Learned();

        byte[] handed = learned.Packed();
        Array.Clear(handed);

        Assert.NotEqual(handed, learned.Packed());
    }

    [Fact(DisplayName = "bytes of any other length than a mark takes are refused")]
    public void BytesOfAnyOtherLengthAreRefused()
    {
        Assert.Throws<ArgumentException>(() => WatermarkMask.Unpacked(new byte[WatermarkMask.PackedBytes - 1]));
        Assert.Throws<ArgumentException>(() => WatermarkMask.Unpacked(new byte[WatermarkMask.PackedBytes + 1]));
    }

    [Fact(DisplayName = "a mark reaching outside the corners is refused, so nothing but a corner is ever looked at")]
    public void AMarkReachingOutsideTheCornersIsRefused()
    {
        byte[] packed = WatermarkPictures.Learned().Packed();
        int middle = ((WatermarkFrame.Height / 2) * WatermarkFrame.Width) + (WatermarkFrame.Width / 2);
        packed[middle >> 3] |= (byte)(1 << (middle & 7));

        Assert.Throws<ArgumentException>(() => WatermarkMask.Unpacked(packed));
        Assert.Throws<ArgumentException>(() => WatermarkMask.Covering([middle]));
    }

    [Fact(DisplayName = "a mark covering nothing is refused, because it would be seen nowhere and everywhere at once")]
    public void AMarkCoveringNothingIsRefused()
    {
        Assert.Throws<ArgumentException>(() => WatermarkMask.Unpacked(new byte[WatermarkMask.PackedBytes]));
        Assert.Throws<ArgumentException>(() => WatermarkMask.Covering([]));
    }
}
