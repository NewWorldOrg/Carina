using Carina.Domain.Encodings;

namespace Carina.Domain.Tests.Encodings;

public sealed class ChapterReadingTests
{
    private static readonly DateTime At = new(2026, 9, 14, 3, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "a reading carries who read it, what they answered and how much of the length they took for breaks, whatever the answer was")]
    public void AReadingCarriesWhoReadItAndWhatTheyAnswered()
    {
        ChapterReading discarded = ChapterReading.Of(
            ChapterDetectorName.Ffmpeg,
            ChapterDetection.Discarded(0.7, "the breaks came to too much of the length"),
            At);
        ChapterReading unasked = ChapterReading.Of(ChapterDetectorName.Nobody, ChapterDetection.NotAsked, At);

        Assert.Equal(ChapterDetectorName.Ffmpeg, discarded.Detector);
        Assert.Equal(ChapterVerdict.Discarded, discarded.Verdict);
        Assert.Equal(0.7, discarded.BreakShare);
        Assert.Equal(At, discarded.DecidedAt);
        Assert.False(discarded.Marks);

        Assert.Equal(ChapterDetectorName.Nobody, unasked.Detector);
        Assert.Equal(ChapterVerdict.NotAsked, unasked.Verdict);
        Assert.Equal(0d, unasked.BreakShare);
    }

    [Fact]
    public void AReadingIsMadeByANamedReaderEndsInANamedVerdictAndTakesAShareOfTheLength()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChapterReading((ChapterDetectorName)7, ChapterVerdict.Marked, 0.1, At));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChapterReading(ChapterDetectorName.Ffmpeg, (ChapterVerdict)9, 0.1, At));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChapterReading(ChapterDetectorName.Ffmpeg, ChapterVerdict.Marked, 1.1, At));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChapterReading(ChapterDetectorName.Ffmpeg, ChapterVerdict.Marked, -0.1, At));
        Assert.Throws<ArgumentException>(() => new ChapterReading(
            ChapterDetectorName.Ffmpeg, ChapterVerdict.Marked, 0.1, new DateTime(2026, 9, 14, 3, 0, 0, DateTimeKind.Local)));
    }
}
