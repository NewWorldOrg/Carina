using Carina.Domain.Encodings;

namespace Carina.Domain.Tests.Encodings;

public sealed class WatermarkLearnerTests
{
    [Fact(DisplayName = "BR-ED2-007: a mark that stays put in a corner while the picture moves is learned, and what moved is not")]
    public void AMarkThatStaysPutWhileThePictureMovesIsLearned()
    {
        WatermarkMask? learned = LearnedFrom(WatermarkLearner.FewestFrames, step => WatermarkPictures.Moving(step, marked: true));

        Assert.NotNull(learned);
        Assert.True(learned.Covers(WatermarkPictures.MarkLeft - 1, 20));
        Assert.True(learned.Covers(WatermarkPictures.MarkRight, 20));
        Assert.False(learned.Covers(WatermarkPictures.MarkLeft + 10, 20));
        Assert.False(learned.Covers(8, 8));
        Assert.All(
            Enumerable.Range(0, WatermarkFrame.Width).Where(x => !InTheMark(x)),
            x => Assert.False(learned.Covers(x, 5)));
    }

    [Fact(DisplayName = "BR-ED2-007: a mark that stays put in the middle of the picture is not taken for a station's watermark")]
    public void AMarkInTheMiddleOfThePictureIsNotAWatermark()
    {
        WatermarkMask? learned = LearnedFrom(
            WatermarkLearner.FewestFrames,
            step => WatermarkPictures.Outlined(WatermarkPictures.Moving(step, marked: false), 200, 120, 280, 150));

        Assert.Null(learned);
    }

    [Fact(DisplayName = "BR-ED2-007: fewer pictures than a learning needs teach nothing")]
    public void TooFewPicturesTeachNothing()
    {
        Assert.Null(LearnedFrom(WatermarkLearner.FewestFrames - 1, step => WatermarkPictures.Moving(step, marked: true)));
        Assert.NotNull(LearnedFrom(WatermarkLearner.FewestFrames, step => WatermarkPictures.Moving(step, marked: true)));
    }

    [Fact(DisplayName = "BR-ED2-007: a mark on screen for less than half of the pictures is not learned, and one on screen for half of them is")]
    public void AMarkOnScreenForLessThanHalfThePicturesIsNotLearned()
    {
        int frames = WatermarkLearner.FewestFrames;

        Assert.Null(LearnedFrom(frames, step => WatermarkPictures.Moving(step, marked: step < (frames / 2) - 1)));
        Assert.NotNull(LearnedFrom(frames, step => WatermarkPictures.Moving(step, marked: step < frames / 2)));
    }

    [Fact(DisplayName = "BR-ED2-007: corners full of detail that never moves are not a watermark, because a station's mark is a small part of them")]
    public void CornersFullOfDetailThatNeverMovesAreNotAWatermark()
    {
        Assert.Null(LearnedFrom(WatermarkLearner.FewestFrames, _ => Checkered()));
    }

    [Fact(DisplayName = "BR-ED2-007: a picture of any other size than the one looked in is refused")]
    public void APictureOfAnyOtherSizeIsRefused()
    {
        var learner = new WatermarkLearner();

        Assert.Throws<ArgumentException>(() => learner.Pictured(new byte[WatermarkFrame.Pixels - 1]));
        Assert.Equal(0, learner.Frames);
    }

    private static WatermarkMask? LearnedFrom(int frames, Func<int, byte[]> picture)
    {
        var learner = new WatermarkLearner();

        for (int step = 0; step < frames; step++)
        {
            learner.Pictured(picture(step));
        }

        Assert.Equal(frames, learner.Frames);

        return learner.Learned();
    }

    private static bool InTheMark(int x) => x >= WatermarkPictures.MarkLeft - 1 && x <= WatermarkPictures.MarkRight;

    private static byte[] Checkered()
    {
        byte[] frame = WatermarkPictures.Plain();

        for (int y = 0; y < WatermarkFrame.Height; y++)
        {
            for (int x = 0; x < WatermarkFrame.Width; x++)
            {
                frame[(y * WatermarkFrame.Width) + x] = ((x / 2) + (y / 2)) % 2 is 0 ? (byte)32 : (byte)224;
            }
        }

        return frame;
    }
}
