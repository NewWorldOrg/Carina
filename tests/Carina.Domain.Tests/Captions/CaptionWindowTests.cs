using Carina.Domain.Captions;

namespace Carina.Domain.Tests.Captions;

public sealed class CaptionWindowTests
{
    private const long Second = CaptionCue.Hertz;

    private static readonly TimeSpan Begins = TimeSpan.FromSeconds(6115.5);

    [Fact]
    public void BrPd017OnTheRecordingItselfEachChangeIsAtItsMomentLessWhereTheFilesClockBegins()
    {
        CaptionRecord record = Record(Shown(6120.5), Cleared(6123.5), Shown(6130));

        CaptionWindow window = CaptionWindow.Of(record, Begins, null, TimeSpan.Zero);

        Assert.Equal([5.0, 8.0, 14.5], window.Captions.Select(caption => caption.At.TotalSeconds));
        Assert.Equal([false, true, false], window.Captions.Select(caption => caption.Picture is null));
        Assert.Equal((1440, 1080), (window.Width, window.Height));
        Assert.Equal(TimeSpan.FromMinutes(10), window.Until);
    }

    [Fact]
    public void BrPd017ACaptionBeforeTheSourcesZeroIsMovedUpToItAndTheLastOfThemIsWhatShowsThere()
    {
        TimeSpan shift = Begins + TimeSpan.FromSeconds(0.5);
        CaptionRecord record = Record(Shown(6115.6, left: 1), Shown(6115.8, left: 2), Cleared(6117));

        CaptionWindow window = CaptionWindow.Of(record, shift, null, TimeSpan.Zero);

        PlacedCaption first = window.Captions[0];
        Assert.Equal(TimeSpan.Zero, first.At);
        Assert.Equal(2, first.Picture!.Left);
        Assert.Equal([0.0, 1.0], window.Captions.Select(caption => Math.Round(caption.At.TotalSeconds, 3)));
    }

    [Fact]
    public void BrPd017ACaptionPastTheEndOfASourceThatSaysHowLongItIsIsLeftOut()
    {
        CaptionRecord record = Record(Shown(6120), Shown(6200));

        CaptionWindow window = CaptionWindow.Of(record, Begins, TimeSpan.FromSeconds(60), TimeSpan.Zero);

        Assert.Equal([4.5], window.Captions.Select(caption => caption.At.TotalSeconds));
    }

    [Fact]
    public void BrPd017TheCaptionAlreadyShowingWhereTheWindowStartsComesFirstAndNothingEarlier()
    {
        CaptionRecord record = Record(Shown(6120), Shown(6130), Shown(6200), Shown(6900));

        CaptionWindow window = CaptionWindow.Of(record, Begins, null, TimeSpan.FromSeconds(30));

        Assert.Equal([14.5, 84.5], window.Captions.Select(caption => caption.At.TotalSeconds));
        Assert.Equal(TimeSpan.FromSeconds(630), window.Until);
    }

    [Fact]
    public void AScreenAlreadyClearedWhereTheWindowStartsBringsNothingIntoIt()
    {
        CaptionRecord record = Record(Shown(6120), Cleared(6125), Shown(6200));

        CaptionWindow window = CaptionWindow.Of(record, Begins, null, TimeSpan.FromSeconds(30));

        Assert.Equal([84.5], window.Captions.Select(caption => caption.At.TotalSeconds));
    }

    [Fact]
    public void AChangeExactlyWhereTheWindowStartsIsTheOneShowingThereAndOneExactlyWhereItEndsIsLeftForTheNext()
    {
        CaptionRecord record = Record(Shown(6120), Shown(6145.5), Shown(6745.5));

        CaptionWindow window = CaptionWindow.Of(record, Begins, null, TimeSpan.FromSeconds(30));

        Assert.Equal([30.0], window.Captions.Select(caption => caption.At.TotalSeconds));
    }

    [Fact]
    public void TenMinutesWithNoChangeAndNothingShowingIsAnEmptyWindowNotAMissingOne()
    {
        CaptionRecord record = Record(Shown(6120), Cleared(6121), Shown(9000));

        CaptionWindow window = CaptionWindow.Of(record, Begins, null, TimeSpan.FromSeconds(60));

        Assert.Empty(window.Captions);
        Assert.Equal(TimeSpan.FromSeconds(660), window.Until);
    }

    [Fact]
    public void BrPd017MomentsFromBeforeTheClockCameAroundAreSubtractedLikeAnyOther()
    {
        CaptionRecord record = new(
            1920,
            1080,
            TimeSpan.FromSeconds(-3.6),
            [new CaptionCue((long)(-3.1 * Second), Picture(0)), new CaptionCue(36_000, null), new CaptionCue((1L << 33) + 10, Picture(1))]);

        CaptionWindow window = CaptionWindow.Of(record, record.StartsAt, null, TimeSpan.Zero);

        Assert.Equal(0.5, Math.Round(window.Captions[0].At.TotalSeconds, 6));
        Assert.Equal(4.0, Math.Round(window.Captions[1].At.TotalSeconds, 6));
        Assert.Equal(2, window.Captions.Count);
    }

    [Fact]
    public void AWindowBeforeTheSourcesZeroIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => CaptionWindow.Of(Record(Shown(6120)), Begins, null, TimeSpan.FromSeconds(-1)));

    private static CaptionRecord Record(params CaptionCue[] cues) => new(1440, 1080, Begins, cues);

    private static CaptionCue Shown(double seconds, int left = 0) => new((long)Math.Round(seconds * Second), Picture(left));

    private static CaptionCue Cleared(double seconds) => new((long)Math.Round(seconds * Second), null);

    private static CaptionPlacement Picture(int left) => new(left, 900, 100, 40, new byte[] { 0x89, 1 });
}
