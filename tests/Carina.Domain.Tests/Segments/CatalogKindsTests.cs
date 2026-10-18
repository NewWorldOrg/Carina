using Carina.Domain.Segments;

using static Carina.Domain.Tests.Segments.CatalogSeries;

namespace Carina.Domain.Tests.Segments;

public sealed class CatalogKindsTests
{
    private static readonly DateTime Evening = new(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc);

    private static readonly CatalogSeries Drama = new(1, "架空の物語", Evening);

    private static readonly CatalogSeries Quiz = new(1, "架空のクイズ", Evening.AddHours(3));

    private static readonly CatalogSeries Elsewhere = new(2, "架空の物語", Evening);

    private static readonly CatalogEntryId Entry = CatalogEntryId.New();

    [Theory(DisplayName = "a piece on the 15-second grid, from a second and a half short of a multiple to half a second over, heard in two series or on two stations is an advert, 0.9")]
    [InlineData(13.5)]
    [InlineData(14.0)]
    [InlineData(15.0)]
    [InlineData(15.5)]
    [InlineData(29.0)]
    [InlineData(29.6)]
    [InlineData(118.5)]
    [InlineData(120.5)]
    public void AGridPieceInTwoSeriesIsAnAdvert(double seconds)
    {
        Assert.Equal(
            new CatalogEstimate(CatalogKind.Cm, 0.9, CatalogDecision.Pattern),
            Estimated(seconds, At(Entry, Drama.Episode(0), 600, seconds), At(Entry, Quiz.Episode(0), 900, seconds)).Estimate);
        Assert.Equal(
            new CatalogEstimate(CatalogKind.Cm, 0.9, CatalogDecision.Pattern),
            Estimated(seconds, At(Entry, Drama.Episode(0), 600, seconds), At(Entry, Elsewhere.Episode(0), 600, seconds)).Estimate);
    }

    [Theory(DisplayName = "a piece off the grid or beyond two minutes in two series is not taken for an advert")]
    [InlineData(12.0)]
    [InlineData(13.4)]
    [InlineData(15.6)]
    [InlineData(22.0)]
    [InlineData(28.4)]
    [InlineData(120.6)]
    [InlineData(135.0)]
    public void APieceOffTheGridIsNotAnAdvert(double seconds)
    {
        CatalogPattern pattern = Estimated(seconds, At(Entry, Drama.Episode(0), 600, seconds), At(Entry, Quiz.Episode(0), 900, seconds));

        Assert.Equal(new CatalogEstimate(CatalogKind.Unknown, null, null), pattern.Estimate);
    }

    [Fact(DisplayName = "a short piece at about the same place from the start of two episodes of one series is a programme fixture, 0.7")]
    public void ASamePlacePieceInOneSeriesIsAProgrammeFixture()
    {
        CatalogPattern pattern = Estimated(20, At(Entry, Drama.Episode(0), 60, 20), At(Entry, Drama.Episode(1), 85, 20));

        Assert.Equal(new CatalogEstimate(CatalogKind.ProgrammeFixture, 0.7, CatalogDecision.Pattern), pattern.Estimate);
    }

    [Fact(DisplayName = "the same short piece at different places in each episode, off the grid, is unknown")]
    public void AShortPieceAtDifferentPlacesIsUnknown()
    {
        CatalogPattern pattern = Estimated(20, At(Entry, Drama.Episode(0), 60, 20), At(Entry, Drama.Episode(1), 400, 20));

        Assert.Equal(CatalogKind.Unknown, pattern.Estimate.Kind);
    }

    [Fact(DisplayName = "a piece of 30 seconds or more at the same place is not a programme fixture")]
    public void ALongerPieceIsNotAProgrammeFixture()
    {
        CatalogPattern pattern = Estimated(31, At(Entry, Drama.Episode(0), 60, 31), At(Entry, Drama.Episode(1), 60, 31));

        Assert.Equal(CatalogKind.Unknown, pattern.Estimate.Kind);
    }

    [Theory(DisplayName = "a brief piece within 15 seconds of the start or end of programmes in two series is a station fixture, 0.6")]
    [InlineData(-8.0)]
    [InlineData(2.0)]
    [InlineData(1795.0)]
    public void ABriefPieceAtTheEdgesOfTwoSeriesIsAStationFixture(double fromProgrammeStart)
    {
        CatalogPattern pattern = Estimated(
            5,
            At(Entry, Drama.Episode(0), fromProgrammeStart, 5),
            At(Entry, Quiz.Episode(0), fromProgrammeStart, 5));

        Assert.Equal(new CatalogEstimate(CatalogKind.StationFixture, 0.6, CatalogDecision.Pattern), pattern.Estimate);
    }

    [Fact(DisplayName = "a brief piece in the middle of programmes, or at the edges of one series only, is no station fixture")]
    public void ABriefPieceInTheMiddleIsNoStationFixture()
    {
        Assert.Equal(CatalogKind.Unknown, Estimated(5, At(Entry, Drama.Episode(0), 600, 5), At(Entry, Quiz.Episode(0), 600, 5)).Estimate.Kind);
        Assert.Equal(CatalogKind.Unknown, Estimated(5, At(Entry, Drama.Episode(0), 2, 5), At(Entry, Quiz.Episode(0), 600, 5)).Estimate.Kind);
        Assert.Equal(
            CatalogKind.ProgrammeFixture,
            Estimated(5, At(Entry, Drama.Episode(0), 2, 5), At(Entry, Drama.Episode(1), 2, 5)).Estimate.Kind);
    }

    [Theory(DisplayName = "a piece on the grid heard in one series only is an advert, 0.5, and one off the grid is unknown")]
    [InlineData(30.0, CatalogKind.Cm)]
    [InlineData(29.0, CatalogKind.Cm)]
    [InlineData(44.0, CatalogKind.Cm)]
    [InlineData(28.4, CatalogKind.Unknown)]
    [InlineData(30.6, CatalogKind.Unknown)]
    public void AGridPieceInOneSeriesIsAnAdvertAtHalf(double seconds, CatalogKind kind)
    {
        CatalogPattern pattern = Estimated(seconds, At(Entry, Drama.Episode(0), 600, seconds), At(Entry, Drama.Episode(1), 900, seconds));

        Assert.Equal(kind, pattern.Estimate.Kind);
        Assert.Equal(kind is CatalogKind.Cm ? 0.5 : null, pattern.Estimate.Confidence);
    }

    [Fact(DisplayName = "the audible core of a 15-second slot, a second short of it, is on the grid")]
    public void TheAudibleCoreOfASlotIsOnTheGrid()
    {
        Assert.True(CatalogKinds.OnTheGrid(TimeSpan.FromSeconds(14.0)));
        Assert.True(CatalogKinds.OnTheGrid(TimeSpan.FromSeconds(29.0)));
        Assert.False(CatalogKinds.OnTheGrid(TimeSpan.FromSeconds(13.4)));
        Assert.False(CatalogKinds.OnTheGrid(TimeSpan.FromSeconds(12.0)));
    }

    [Fact(DisplayName = "the rows are taken from the top: two series beat a same-place fixture, and a same-place fixture beats one series on the grid")]
    public void TheRowsAreTakenFromTheTop()
    {
        Assert.Equal(
            new CatalogEstimate(CatalogKind.Cm, 0.9, CatalogDecision.Pattern),
            Estimated(15, At(Entry, Drama.Episode(0), 60, 15), At(Entry, Quiz.Episode(0), 60, 15)).Estimate);
        Assert.Equal(
            new CatalogEstimate(CatalogKind.ProgrammeFixture, 0.7, CatalogDecision.Pattern),
            Estimated(15, At(Entry, Drama.Episode(0), 60, 15), At(Entry, Drama.Episode(1), 70, 15)).Estimate);
    }

    [Fact(DisplayName = "an answer given on one of the stretches decides the kind outright, 1.0")]
    public void AnAnswerDecidesTheKind()
    {
        CatalogPattern breakAnswered = Estimated(
            22,
            At(Entry, Drama.Episode(0), 600, 22, SegmentKind.Break),
            At(Entry, Drama.Episode(1), 900, 22));
        CatalogPattern programmeAnswered = Estimated(
            15,
            At(Entry, Drama.Episode(0), 600, 15, SegmentKind.Programme),
            At(Entry, Quiz.Episode(0), 900, 15));

        Assert.Equal(new CatalogEstimate(CatalogKind.Cm, 1.0, CatalogDecision.Answer), breakAnswered.Estimate);
        Assert.Equal(new CatalogEstimate(CatalogKind.ProgrammeFixture, 1.0, CatalogDecision.Answer), programmeAnswered.Estimate);
    }

    [Fact(DisplayName = "an opening is not estimated in this phase: its sound falls to the rows below")]
    public void AnOpeningIsNotEstimated()
    {
        PlacedOccurrence[] offTheGrid = [.. Enumerable.Range(0, 3).Select(week => At(Entry, Drama.Episode(week), 0, 85))];
        PlacedOccurrence[] onTheGrid = [.. Enumerable.Range(0, 3).Select(week => At(Entry, Drama.Episode(week), 0, 90))];
        PlacedOccurrence[] closing = [.. Enumerable.Range(0, 3).Select(week => At(Entry, Drama.Episode(week), 1700, 85))];

        Assert.Equal(new CatalogEstimate(CatalogKind.Unknown, null, null), Estimated(85, offTheGrid).Estimate);
        Assert.Equal(new CatalogEstimate(CatalogKind.Cm, 0.5, CatalogDecision.Pattern), Estimated(90, onTheGrid).Estimate);
        Assert.Equal(new CatalogEstimate(CatalogKind.Unknown, null, null), Estimated(85, closing).Estimate);
    }

    [Fact(DisplayName = "a stretch that covers only part of the entry does not count towards its kind")]
    public void APartialStretchDoesNotCount()
    {
        CatalogPattern pattern = Estimated(
            30,
            At(Entry, Drama.Episode(0), 600, 30),
            At(Entry, Drama.Episode(1), 900, 30),
            At(Entry, Quiz.Episode(0), 600, 15));

        Assert.Equal(new CatalogEstimate(CatalogKind.Cm, 0.5, CatalogDecision.Pattern), pattern.Estimate);
        Assert.Equal(1, pattern.SeriesCount);
        Assert.Equal(1, pattern.StationCount);
    }

    [Fact(DisplayName = "recordings that share programme fixture sound count as one series")]
    public void FixtureSoundJoinsSeries()
    {
        CatalogSeries renamed = new(1, "まるで違う架空の名前", Evening);
        PlacedOccurrence first = At(Entry, Drama.Episode(0), 600, 15);
        PlacedOccurrence second = At(Entry, renamed.Episode(1), 900, 15);
        SeriesLinks links = SeriesLinks.SharingSound([[first.Occurrence.RecordingId, second.Occurrence.RecordingId]]);

        Assert.Equal(CatalogKind.Cm, CatalogKinds.Estimate(TimeSpan.FromSeconds(15), [first, second], SeriesLinks.None).Estimate.Kind);
        Assert.Equal(0.9, CatalogKinds.Estimate(TimeSpan.FromSeconds(15), [first, second], SeriesLinks.None).Estimate.Confidence);
        Assert.Equal(0.5, CatalogKinds.Estimate(TimeSpan.FromSeconds(15), [first, second], links).Estimate.Confidence);
    }

    private static CatalogPattern Estimated(double seconds, params PlacedOccurrence[] occurrences)
        => CatalogKinds.Estimate(TimeSpan.FromSeconds(seconds), occurrences, SeriesLinks.None);
}
