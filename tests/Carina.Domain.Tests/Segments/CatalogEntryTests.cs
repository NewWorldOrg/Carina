using Carina.Domain.Segments;

using static Carina.Domain.Tests.Segments.CatalogSeries;

namespace Carina.Domain.Tests.Segments;

public sealed class CatalogEntryTests
{
    private static readonly DateTime Evening = new(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc);

    private static readonly CatalogSeries Drama = new(1, "架空の物語", Evening);

    private static readonly CatalogSeries Quiz = new(1, "架空のクイズ", Evening.AddHours(3));

    private static readonly CatalogSeries Elsewhere = new(2, "架空の物語", Evening);

    private static readonly uint[] Fifteen = [.. Enumerable.Range(1, 938).Select(value => (uint)value)];

    [Fact(DisplayName = "an entry started from its first stretches is estimated and counted at once")]
    public void AnEntryIsEstimatedWhenStarted()
    {
        CatalogEntryId id = CatalogEntryId.New();

        CatalogEntry entry = CatalogEntry.Start(
            id,
            Fifteen,
            [At(id, Drama.Episode(0), 600, 15), At(id, Elsewhere.Episode(2), 900, 15)],
            SeriesLinks.None,
            Now);

        Assert.Equal(id, entry.Id);
        Assert.Equal(CatalogKind.Cm, entry.Kind);
        Assert.Equal(0.9, entry.Confidence);
        Assert.Equal(CatalogDecision.Pattern, entry.DecidedBy);
        Assert.Equal(TimeSpan.FromMilliseconds(938 * 16), entry.Length);
        Assert.Equal(Fifteen, entry.Frames());
        Assert.Equal(938 * sizeof(uint), entry.Fingerprint.Length);
        Assert.Equal(2, entry.OccurrenceCount);
        Assert.Equal(2, entry.SeriesCount);
        Assert.Equal(2, entry.StationCount);
        Assert.Equal(Drama.Episode(0).RecordingStartedAt, entry.FirstSeenAt);
        Assert.Equal(Elsewhere.Episode(2).RecordingStartedAt, entry.LastSeenAt);
        Assert.Equal(Now, entry.UpdatedAt);
    }

    [Fact(DisplayName = "an entry is estimated again as stretches are added, and counts every stretch it has")]
    public void AnEntryIsEstimatedAgainAsStretchesAreAdded()
    {
        CatalogEntryId id = CatalogEntryId.New();
        PlacedOccurrence[] first = [At(id, Drama.Episode(0), 600, 15), At(id, Drama.Episode(1), 900, 15)];
        CatalogEntry entry = CatalogEntry.Start(id, Fifteen, first, SeriesLinks.None, Now);

        Assert.Equal(0.5, entry.Confidence);

        entry.Reestimate([.. first, At(id, Quiz.Episode(3), 600, 15), At(id, Quiz.Episode(4), 600, 7)], SeriesLinks.None, Now.AddHours(1));

        Assert.Equal(0.9, entry.Confidence);
        Assert.Equal(4, entry.OccurrenceCount);
        Assert.Equal(2, entry.SeriesCount);
        Assert.Equal(1, entry.StationCount);
        Assert.Equal(Quiz.Episode(4).RecordingStartedAt, entry.LastSeenAt);
        Assert.Equal(Now.AddHours(1), entry.UpdatedAt);
    }

    [Fact(DisplayName = "the opening's song in another series' advert keeps the entry an opening and makes that stretch alone an advert")]
    public void AnOpeningFragmentInAnotherSeriesIsAnAdvertThere()
    {
        CatalogEntryId id = CatalogEntryId.New();
        PlacedOccurrence answered = At(id, Drama.Episode(0), 0, 90, SegmentKind.Opening);
        PlacedOccurrence again = At(id, Drama.Episode(1), 0, 90);
        PlacedOccurrence advertised = At(id, Quiz.Episode(0), 600, 15);
        PlacedOccurrence cutShortAtHome = At(id, Drama.Episode(2), 0, 45);

        CatalogEntry entry = CatalogEntry.Start(id, [.. Enumerable.Range(1, 5625).Select(value => (uint)value)], [answered, again, advertised, cutShortAtHome], SeriesLinks.None, Now);

        Assert.Equal(CatalogKind.Opening, entry.Kind);
        Assert.Equal(CatalogDecision.Answer, entry.DecidedBy);
        Assert.Equal(SegmentKind.Opening, answered.Occurrence.KindOverride);
        Assert.Null(again.Occurrence.KindOverride);
        Assert.Equal(SegmentKind.Break, advertised.Occurrence.KindOverride);
        Assert.Null(cutShortAtHome.Occurrence.KindOverride);
    }

    [Theory(DisplayName = "the opening's song in another series is an advert there from a second and a half short of the grid, and not further short")]
    [InlineData(14.0, SegmentKind.Break)]
    [InlineData(13.4, null)]
    public void AnOpeningFragmentIsAnAdvertWithinTheGridWindow(double seconds, SegmentKind? taken)
    {
        CatalogEntryId id = CatalogEntryId.New();
        PlacedOccurrence answered = At(id, Drama.Episode(0), 0, 90, SegmentKind.Opening);
        PlacedOccurrence again = At(id, Drama.Episode(1), 0, 90);
        PlacedOccurrence advertised = At(id, Quiz.Episode(0), 600, seconds);

        CatalogEntry.Start(id, [.. Enumerable.Range(1, 5625).Select(value => (uint)value)], [answered, again, advertised], SeriesLinks.None, Now);

        Assert.Equal(taken, advertised.Occurrence.KindOverride);
    }

    [Fact(DisplayName = "an override that no longer holds is cleared when the entry is estimated again")]
    public void AnOverrideThatNoLongerHoldsIsCleared()
    {
        CatalogEntryId id = CatalogEntryId.New();
        PlacedOccurrence answered = At(id, Drama.Episode(0), 0, 90, SegmentKind.Opening);
        PlacedOccurrence again = At(id, Drama.Episode(1), 0, 90);
        PlacedOccurrence advertised = At(id, Quiz.Episode(0), 600, 15);
        CatalogEntry entry = CatalogEntry.Start(id, [.. Enumerable.Range(1, 5625).Select(value => (uint)value)], [answered, again, advertised], SeriesLinks.None, Now);

        entry.Reestimate(
            [PlacedOccurrence.Of(answered.Occurrence, Drama.Episode(0), null), again, advertised],
            SeriesLinks.None,
            Now.AddHours(1));

        Assert.Equal(CatalogKind.Unknown, entry.Kind);
        Assert.Null(answered.Occurrence.KindOverride);
        Assert.Null(advertised.Occurrence.KindOverride);
    }

    [Fact(DisplayName = "an entry is made of at least two stretches, all of its own")]
    public void AnEntryNeedsTwoStretchesOfItsOwn()
    {
        CatalogEntryId id = CatalogEntryId.New();

        Assert.Throws<ArgumentException>(() => CatalogEntry.Start(id, Fifteen, [At(id, Drama.Episode(0), 600, 15)], SeriesLinks.None, Now));
        Assert.Throws<ArgumentException>(
            () => CatalogEntry.Start(id, Fifteen, [At(id, Drama.Episode(0), 600, 15), At(CatalogEntryId.New(), Drama.Episode(1), 600, 15)], SeriesLinks.None, Now));
    }

    [Fact(DisplayName = "a fingerprint holds at least one frame and no more than three minutes")]
    public void AFingerprintIsBounded()
    {
        CatalogEntryId id = CatalogEntryId.New();
        PlacedOccurrence[] two = [At(id, Drama.Episode(0), 600, 15), At(id, Drama.Episode(1), 600, 15)];

        Assert.Throws<ArgumentException>(() => CatalogEntry.Start(id, [], two, SeriesLinks.None, Now));
        Assert.Throws<ArgumentException>(() => CatalogEntry.Start(id, new uint[CatalogEntry.MostFrames + 1], two, SeriesLinks.None, Now));
    }

    [Fact(DisplayName = "an unknown entry has neither a confidence nor a reason, and every other kind has both")]
    public void AnUnknownEntryHasNoConfidence()
    {
        Assert.Throws<ArgumentException>(() => Rehydrated(CatalogKind.Unknown, 0.5, CatalogDecision.Pattern));
        Assert.Throws<ArgumentException>(() => Rehydrated(CatalogKind.Cm, null, null));
        Assert.Throws<ArgumentException>(() => Rehydrated(CatalogKind.Cm, 0.9, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rehydrated(CatalogKind.Cm, 1.1, CatalogDecision.Pattern));

        Assert.Equal(CatalogKind.Unknown, Rehydrated(CatalogKind.Unknown, null, null).Kind);
        Assert.Equal(0.9, Rehydrated(CatalogKind.Cm, 0.9, CatalogDecision.Pattern).Confidence);
    }

    [Fact(DisplayName = "a kept entry counts at least two stretches, one series and one station, and was seen in order")]
    public void AKeptEntryHoldsItsCounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Rehydrated(CatalogKind.Cm, 0.9, CatalogDecision.Pattern, occurrences: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rehydrated(CatalogKind.Cm, 0.9, CatalogDecision.Pattern, seriesCount: 0));
        Assert.Throws<ArgumentException>(() => Rehydrated(CatalogKind.Cm, 0.9, CatalogDecision.Pattern, lastSeenAt: Evening.AddDays(-1)));
        Assert.Throws<ArgumentException>(() => Rehydrated(CatalogKind.Cm, 0.9, CatalogDecision.Pattern, lastSeenAt: DateTime.SpecifyKind(Evening, DateTimeKind.Local)));
    }

    private static CatalogEntry Rehydrated(
        CatalogKind kind,
        double? confidence,
        CatalogDecision? decidedBy,
        int occurrences = 2,
        int seriesCount = 1,
        DateTime? lastSeenAt = null)
        => CatalogEntry.Rehydrate(
            CatalogEntryId.New(),
            kind,
            confidence,
            decidedBy,
            TimeSpan.FromSeconds(15),
            CatalogFingerprint.Write(Fifteen),
            occurrences,
            seriesCount,
            1,
            Evening,
            lastSeenAt ?? Evening.AddDays(7),
            Now);
}
