using Carina.Domain.Encodings;

namespace Carina.Domain.Tests.Encodings;

public sealed class ChapterGridWatermarkTests
{
    private static readonly TimeSpan HalfAnHour = TimeSpan.FromMinutes(30);

    [Fact(DisplayName = "BR-ED2-007: a pod the watermark learned ahead stayed on screen through is programme, and a pod it was off through is still a break")]
    public void APodTheWatermarkStayedOnScreenThroughIsProgramme()
    {
        ChapterEvidence seen = TwoPods() with
        {
            Sightings = EverySecond(at => !Between(at, 900, 960) && !Between(at, 1500, 1800)),
        };

        ChapterDetection read = ChapterGrid.Mark(seen, HalfAnHour, new ChapterSettings());

        Assert.Equal(ChapterVerdict.Marked, read.Verdict);
        ChapterSegment gap = Assert.Single(read.Segments, segment => segment.Kind is ChapterKind.Break);
        Assert.Equal(new ChapterSegment(At(900), At(960), ChapterKind.Break), gap);
        Assert.Equal(60d / 1800d, read.BreakShare, 6);
        Assert.Contains("1 of the 2 candidate breaks were taken away", read.Note, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-ED2-007: with no watermark learned ahead every pod stands as it did, and nothing is said about a watermark")]
    public void WithNoWatermarkLearnedAheadEveryPodStands()
    {
        ChapterDetection read = ChapterGrid.Mark(TwoPods(), HalfAnHour, new ChapterSettings());

        Assert.Equal(ChapterVerdict.Marked, read.Verdict);
        Assert.Equal(2, read.Breaks);
        Assert.Equal(string.Empty, read.Note);
    }

    [Fact(DisplayName = "BR-ED2-007: a watermark on screen in nearly every picture tells programme from break nowhere, so it takes nothing away and says why")]
    public void AWatermarkOnScreenInNearlyEveryPictureIsNotUsed()
    {
        ChapterEvidence seen = TwoPods() with { Sightings = EverySecond(at => !Between(at, 1750, 1800)) };

        ChapterDetection read = ChapterGrid.Mark(seen, HalfAnHour, new ChapterSettings());

        Assert.Equal(2, read.Breaks);
        Assert.Contains("was not used", read.Note, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-ED2-007: a pod with too few pictures looked at inside it is not taken away on their word")]
    public void APodWithTooFewPicturesInsideIsNotTakenAway()
    {
        ChapterEvidence seen = Observed(At(300), At(360)) with
        {
            Sightings =
            [
                new WatermarkSighting(At(100), false),
                new WatermarkSighting(At(330), true),
                new WatermarkSighting(At(331), true),
            ],
        };

        ChapterDetection read = ChapterGrid.Mark(seen, HalfAnHour, new ChapterSettings());

        Assert.Equal(1, read.Breaks);
    }

    [Fact(DisplayName = "BR-ED2-007: the pictures at the very edges of a pod are not counted, because the watermark comes and goes around the boundary itself")]
    public void ThePicturesAtTheEdgesOfAPodAreNotCounted()
    {
        ChapterEvidence seen = Observed(At(300), At(360)) with
        {
            Sightings =
            [
                .. Enumerable.Range(0, 300).Select(second => new WatermarkSighting(At(second), false)),
                .. Enumerable.Range(0, 41).Select(step => new WatermarkSighting(At(300 + (step * 0.05)), true)),
                .. Enumerable.Range(303, 55).Select(second => new WatermarkSighting(At(second), false)),
                .. Enumerable.Range(0, 41).Select(step => new WatermarkSighting(At(358 + (step * 0.05)), true)),
                .. Enumerable.Range(361, 1439).Select(second => new WatermarkSighting(At(second), false)),
            ],
        };

        Assert.True(seen.Sightings.Count(sighting => sighting.At >= At(300) && sighting.At <= At(360) && sighting.Seen) > 55);

        ChapterDetection read = ChapterGrid.Mark(seen, HalfAnHour, new ChapterSettings());

        Assert.Equal(1, read.Breaks);
    }

    [Fact(DisplayName = "BR-ED2-007: when the watermark takes every pod away the reading found nothing, and says why")]
    public void WhenTheWatermarkTakesEveryPodAwayTheReadingFoundNothing()
    {
        ChapterEvidence seen = TwoPods() with { Sightings = EverySecond(at => !Between(at, 1500, 1800)) };

        ChapterDetection read = ChapterGrid.Mark(seen, HalfAnHour, new ChapterSettings());

        Assert.Equal(ChapterVerdict.NothingFound, read.Verdict);
        Assert.Contains("2 of the 2 candidate breaks were taken away", read.Note, StringComparison.Ordinal);
    }

    private static ChapterEvidence TwoPods() => Observed(At(300), At(360), At(900), At(960));

    private static IReadOnlyList<WatermarkSighting> EverySecond(Func<TimeSpan, bool> seen)
        => [.. Enumerable.Range(0, 1800).Select(second => new WatermarkSighting(At(second), seen(At(second))))];

    private static bool Between(TimeSpan at, double from, double until) => at >= At(from) && at <= At(until);

    private static TimeSpan At(double second) => TimeSpan.FromSeconds(second);

    private static ChapterEvidence Observed(params TimeSpan[] boundaries)
        => new()
        {
            Silences = [.. boundaries.Select(around => new ChapterSpan(around - At(0.25), around + At(0.25)))],
            Scenes = [.. boundaries.Select(at => new ChapterScene(at, 0.9))],
        };
}
