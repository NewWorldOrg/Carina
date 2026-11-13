using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using static Carina.Domain.Tests.Unit.Segments.CatalogSeries;

namespace Carina.Domain.Tests.Unit.Segments;

[Trait("Category", "Unit")]
public sealed class CatalogKindsTests
{
    private static readonly DateTime Evening = new(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc);

    private static readonly CatalogSeries Drama = new(1, "架空の物語", Evening);

    private static readonly CatalogSeries Quiz = new(1, "架空のクイズ", Evening.AddHours(3));

    private static readonly CatalogSeries Elsewhere = new(2, "架空の物語", Evening);

    private static readonly CatalogEntryId Entry = CatalogEntryId.New();

    /// <summary>
    /// The evening a synthetic weekly series of half an hour airs, whose opening is matched in pieces: a long head, a
    /// short phrase that the programme plays again in its middle, and a slot of 30 seconds on the grid. The first
    /// episode is recorded twice, the second recording starting a few seconds after the first.
    /// </summary>
    private static readonly DateTime LateEvening = new(2026, 10, 5, 13, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Lead = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan Runs = TimeSpan.FromMinutes(30);

    private static readonly DateTime Now = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The night a synthetic weekly late-night series airs, whose sponsor's advert is put in the same break every week,
    /// so that it is heard at about the same place from the start of each episode as a fixed part of the programme is.
    /// Each week has a recording of its own, and a quiz on the same station shares the adverts heard in two series.
    /// </summary>
    private static readonly DateTime Night = new(2026, 10, 5, 15, 0, 0, DateTimeKind.Utc);

    private static readonly CatalogSeries Variety = new(1, "架空の深夜番組", Night);

    private static readonly CatalogSeries QuizBeforeVariety = new(1, "架空のクイズ", Night.AddHours(-5));

    private readonly Episode first = new(Programme(1, "架空の物語", LateEvening, Lead), RecordingId.New());

    private readonly Episode again = new(Programme(1, null, LateEvening, Lead - TimeSpan.FromSeconds(4)), RecordingId.New());

    private readonly Episode second = new(Programme(1, "架空の物語", LateEvening.AddDays(7), Lead), RecordingId.New());

    private readonly RecordingId[] recordingsOfTheWeeks = [RecordingId.New(), RecordingId.New(), RecordingId.New()];

    private Episode[] Weeks => [first, again, second];

    [Theory(DisplayName = "a piece on the 15-second grid, from a second and a half short of a multiple to half a second over, heard in two series or on two stations is an advert, 0.9")]
    [InlineData(13.5)]
    [InlineData(14.0)]
    [InlineData(15.0)]
    [InlineData(15.5)]
    [InlineData(29.0)]
    [InlineData(29.6)]
    [InlineData(89.0)]
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
        CatalogPattern early = Estimated(31, At(Entry, Drama.Episode(0), 60, 31), At(Entry, Drama.Episode(1), 60, 31));
        CatalogPattern middle = Estimated(31, At(Entry, Drama.Episode(0), Midpoint - 30, 31), At(Entry, Drama.Episode(1), Midpoint - 5, 31));

        Assert.Equal(CatalogKind.Opening, early.Estimate.Kind);
        Assert.Equal(CatalogKind.Unknown, middle.Estimate.Kind);
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

    [Theory(DisplayName = "a piece on the grid of up to a minute heard in one series once in each half is an advert, 0.5, and one off the grid or longer is unknown")]
    [InlineData(30.0, CatalogKind.Cm)]
    [InlineData(29.0, CatalogKind.Cm)]
    [InlineData(44.0, CatalogKind.Cm)]
    [InlineData(59.0, CatalogKind.Cm)]
    [InlineData(60.0, CatalogKind.Cm)]
    [InlineData(60.5, CatalogKind.Cm)]
    [InlineData(28.4, CatalogKind.Unknown)]
    [InlineData(30.6, CatalogKind.Unknown)]
    [InlineData(61.0, CatalogKind.Unknown)]
    [InlineData(75.0, CatalogKind.Unknown)]
    [InlineData(89.0, CatalogKind.Unknown)]
    public void AGridPieceInOneSeriesOnceInEachHalfIsAnAdvertAtHalf(double seconds, CatalogKind kind)
    {
        CatalogPattern pattern = Estimated(seconds, At(Entry, Drama.Episode(0), Midpoint - 300, seconds), At(Entry, Drama.Episode(1), Midpoint, seconds));

        Assert.Equal(kind, pattern.Estimate.Kind);
        Assert.Equal(kind is CatalogKind.Cm ? 0.5 : null, pattern.Estimate.Confidence);
    }

    [Theory(DisplayName = "BR-CD-005: a piece on the grid of up to a minute heard in one series twice at the start or twice in the middle is an advert, 0.5, and a longer one is the opening only at the start")]
    [InlineData(30.0, CatalogKind.Cm, CatalogKind.Cm)]
    [InlineData(29.0, CatalogKind.Cm, CatalogKind.Cm)]
    [InlineData(44.0, CatalogKind.Cm, CatalogKind.Cm)]
    [InlineData(45.0, CatalogKind.Cm, CatalogKind.Cm)]
    [InlineData(59.0, CatalogKind.Cm, CatalogKind.Cm)]
    [InlineData(60.0, CatalogKind.Cm, CatalogKind.Cm)]
    [InlineData(60.5, CatalogKind.Cm, CatalogKind.Cm)]
    [InlineData(28.4, CatalogKind.Unknown, CatalogKind.Unknown)]
    [InlineData(30.6, CatalogKind.Opening, CatalogKind.Unknown)]
    [InlineData(61.0, CatalogKind.Opening, CatalogKind.Unknown)]
    [InlineData(75.0, CatalogKind.Opening, CatalogKind.Unknown)]
    [InlineData(89.0, CatalogKind.Opening, CatalogKind.Unknown)]
    public void AGridPieceInOneSeriesTwiceInOneHalfIsAnAdvertAtHalf(double seconds, CatalogKind atTheStart, CatalogKind inTheMiddle)
    {
        CatalogPattern start = Estimated(seconds, At(Entry, Drama.Episode(0), 0, seconds), At(Entry, Drama.Episode(1), 60, seconds));
        CatalogPattern middle = Estimated(seconds, At(Entry, Drama.Episode(0), Midpoint - 300, seconds), At(Entry, Drama.Episode(1), Midpoint - 200, seconds));

        Assert.Equal((atTheStart, inTheMiddle), (start.Estimate.Kind, middle.Estimate.Kind));
        Assert.Equal(atTheStart is CatalogKind.Cm ? 0.5 : atTheStart is CatalogKind.Opening ? 0.8 : null, start.Estimate.Confidence);
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

    [Theory(DisplayName = "BR-CD-005: a piece of 30 seconds to 3 minutes starting within six minutes after the guide's start of two episodes of one series is its opening, 0.8")]
    [InlineData(30.6, 0)]
    [InlineData(85.0, 0)]
    [InlineData(85.0, -15)]
    [InlineData(85.0, 360)]
    [InlineData(89.0, 0)]
    [InlineData(90.0, 120)]
    [InlineData(180.0, 300)]
    public void APieceInTheFirstHalfOfEachEpisodeIsTheOpening(double seconds, double fromProgrammeStart)
    {
        CatalogPattern pattern = Estimated(seconds, Weekly(Drama, 3, fromProgrammeStart, seconds));

        Assert.Equal(new CatalogEstimate(CatalogKind.Opening, 0.8, CatalogDecision.Pattern), pattern.Estimate);
        Assert.Equal(1, pattern.SeriesCount);
        Assert.All(pattern.Overrides, Assert.Null);
    }

    [Theory(DisplayName = "BR-CD-005: the same piece ending within six minutes before the guide's end of two episodes is the ending, 0.8")]
    [InlineData(89.0, 1700)]
    [InlineData(100.0, 1650)]
    [InlineData(85.0, 1355)]
    [InlineData(85.0, 1730)]
    public void APieceInTheSecondHalfOfEachEpisodeIsTheEnding(double seconds, double fromProgrammeStart)
    {
        CatalogPattern pattern = Estimated(seconds, Weekly(Drama, 2, fromProgrammeStart, seconds));

        Assert.Equal(new CatalogEstimate(CatalogKind.Ending, 0.8, CatalogDecision.Pattern), pattern.Estimate);
    }

    [Theory(DisplayName = "a piece shorter than 30 seconds, longer than 3 minutes, or on the grid of up to a minute is neither an opening nor an ending")]
    [InlineData(31.0, CatalogKind.Opening, CatalogKind.Ending)]
    [InlineData(29.9, CatalogKind.Cm, CatalogKind.Cm)]
    [InlineData(45.0, CatalogKind.Cm, CatalogKind.Cm)]
    [InlineData(60.0, CatalogKind.Cm, CatalogKind.Cm)]
    [InlineData(25.0, CatalogKind.Unknown, CatalogKind.Unknown)]
    [InlineData(180.0, CatalogKind.Opening, CatalogKind.Ending)]
    [InlineData(180.1, CatalogKind.Unknown, CatalogKind.Unknown)]
    public void APieceOutsideTheLengthsIsNoTheme(double seconds, CatalogKind early, CatalogKind late)
    {
        CatalogPattern first = Estimated(seconds, At(Entry, Drama.Episode(0), 0, seconds), At(Entry, Drama.Episode(1), 200, seconds));
        CatalogPattern second = Estimated(seconds, At(Entry, Drama.Episode(0), 1450, seconds), At(Entry, Drama.Episode(1), 1500, seconds));

        Assert.Equal((early, late), (first.Estimate.Kind, second.Estimate.Kind));
    }

    [Fact(DisplayName = "BR-CD-005: a piece of 90 seconds heard twice from minute 2 is the opening, twice up to 2 minutes before the end the ending, and twice from minute 12 neither")]
    public void AThemeIsPlacedByTheGuidesStartAndEnd()
    {
        Assert.Equal(new CatalogEstimate(CatalogKind.Opening, 0.8, CatalogDecision.Pattern), Estimated(90, Weekly(Drama, 2, 120, 90)).Estimate);
        Assert.Equal(new CatalogEstimate(CatalogKind.Ending, 0.8, CatalogDecision.Pattern), Estimated(90, Weekly(Drama, 2, Runs.TotalSeconds - 120 - 90, 90)).Estimate);
        Assert.Equal(CatalogEstimate.Unknown, Estimated(85, Weekly(Drama, 2, 720, 85)).Estimate);
        Assert.Equal(CatalogEstimate.Unknown, Estimated(90, Weekly(Drama, 2, 720, 90)).Estimate);
    }

    [Fact(DisplayName = "a piece heard in one episode, or once in each half, is neither an opening nor an ending")]
    public void APieceNotRepeatedInOneHalfIsNoTheme()
    {
        Assert.Equal(CatalogKind.Unknown, Estimated(89, At(Entry, Drama.Episode(0), 0, 89), At(Entry, Drama.Episode(0), 1700, 89)).Estimate.Kind);
        Assert.Equal(CatalogKind.Unknown, Estimated(89, At(Entry, Drama.Episode(0), 0, 89), At(Entry, Drama.Episode(1), 1700, 89)).Estimate.Kind);
    }

    [Fact(DisplayName = "the half more of the episodes hear it in decides between opening and ending, the opening on a tie")]
    public void TheHalfHeardMoreOftenDecides()
    {
        PlacedOccurrence[] mostlyAtTheEnd = [.. Weekly(Drama, 2, 0, 89), .. Weekly(Drama, 3, 1700, 89, firstWeek: 2)];
        PlacedOccurrence[] even = [.. Weekly(Drama, 2, 0, 89), .. Weekly(Drama, 2, 1700, 89, firstWeek: 2)];

        Assert.Equal(CatalogKind.Ending, Estimated(89, mostlyAtTheEnd).Estimate.Kind);
        Assert.Equal(CatalogKind.Opening, Estimated(89, even).Estimate.Kind);
    }

    [Fact(DisplayName = "BR-CD-005: the opening sung at the end of the last episode is that episode's ending, and the series' opening still")]
    public void TheOpeningAtTheEndOfTheLastEpisodeIsItsEnding()
    {
        PlacedOccurrence[] weeks = Weekly(Drama, 3, 0, 89);
        PlacedOccurrence last = At(Entry, Drama.Episode(3), 1710, 89);

        CatalogPattern pattern = Estimated(89, [.. weeks, last]);

        Assert.Equal(new CatalogEstimate(CatalogKind.Opening, 0.8, CatalogDecision.Pattern), pattern.Estimate);
        Assert.Equal([null, null, null, SegmentKind.Ending], pattern.Overrides);
    }

    [Fact(DisplayName = "BR-CD-005: the opening sung in the middle of an episode of its series, an insert song, is the programme there, and the series' opening still")]
    public void TheOpeningInTheMiddleOfAnEpisodeIsTheProgramme()
    {
        PlacedOccurrence inserted = At(Entry, Drama.Episode(2), Midpoint - 120, 89);

        CatalogPattern pattern = Estimated(89, [.. Weekly(Drama, 2, 0, 89), inserted]);

        Assert.Equal(new CatalogEstimate(CatalogKind.Opening, 0.8, CatalogDecision.Pattern), pattern.Estimate);
        Assert.Equal([null, null, SegmentKind.Programme], pattern.Overrides);
    }

    [Fact(DisplayName = "a last episode with another opening leaves the series' opening as it was, and an ending sung at the start of the first episode is its opening")]
    public void ALastEpisodeWithAnotherOpeningLeavesTheSeriesOpening()
    {
        CatalogPattern opening = Estimated(89, Weekly(Drama, 3, 0, 89));
        CatalogPattern ending = Estimated(89, [At(Entry, Drama.Episode(0), 0, 89), .. Weekly(Drama, 3, 1700, 89, firstWeek: 1)]);

        Assert.Equal(CatalogKind.Opening, opening.Estimate.Kind);
        Assert.All(opening.Overrides, Assert.Null);
        Assert.Equal(CatalogKind.Ending, ending.Estimate.Kind);
        Assert.Equal([SegmentKind.Opening, null, null, null], ending.Overrides);
    }

    [Theory(DisplayName = "BR-CD-005: part of the opening heard in another series' break leaves the entry an opening and is an advert there, when it is on the grid")]
    [InlineData(14.0, SegmentKind.Break)]
    [InlineData(29.0, SegmentKind.Break)]
    [InlineData(20.0, null)]
    public void PartOfTheOpeningInAnotherSeriesIsAnAdvertThere(double seconds, SegmentKind? taken)
    {
        PlacedOccurrence advertised = At(Entry, Quiz.Episode(0), 600, seconds);
        PlacedOccurrence cutShortAtHome = At(Entry, Drama.Episode(2), 0, seconds);

        CatalogPattern pattern = Estimated(89, [.. Weekly(Drama, 2, 0, 89), advertised, cutShortAtHome]);

        Assert.Equal(new CatalogEstimate(CatalogKind.Opening, 0.8, CatalogDecision.Pattern), pattern.Estimate);
        Assert.Equal((1, 1), (pattern.SeriesCount, pattern.StationCount));
        Assert.Equal([null, null, taken, null], pattern.Overrides);
    }

    [Fact(DisplayName = "BR-CD-011: an episode called otherwise in the slot of the series, hearing the opening in the same half, is joined to the series by the sound")]
    public void ARenamedEpisodeInTheSlotIsJoinedByTheSound()
    {
        CatalogSeries renamed = new(1, "まるで違う架空の名前", Evening.AddMinutes(30));
        CatalogPattern once = Estimated(89, [.. Weekly(Drama, 2, 0, 89), At(Entry, renamed.Episode(2), 0, 89)]);
        CatalogPattern twice = Estimated(89, [.. Weekly(Drama, 2, 0, 89), At(Entry, renamed.Episode(2), 0, 89), At(Entry, renamed.Episode(3), 30, 89)]);

        Assert.Equal(new CatalogEstimate(CatalogKind.Cm, 0.9, CatalogDecision.Pattern), Estimated(89, At(Entry, Drama.Episode(0), 0, 89), At(Entry, renamed.Episode(2), 0, 89)).Estimate);
        Assert.Equal(new CatalogEstimate(CatalogKind.Opening, 0.8, CatalogDecision.Pattern), once.Estimate);
        Assert.Equal((1, 1), (once.SeriesCount, once.StationCount));
        Assert.Equal(new CatalogEstimate(CatalogKind.Opening, 0.8, CatalogDecision.Pattern), twice.Estimate);
        Assert.Equal(1, twice.SeriesCount);
    }

    [Fact(DisplayName = "BR-CD-011: a series in another slot, or one that hears the sound in the other half, is not joined, and a sound on the grid in two series is an advert")]
    public void OnlyTheSlotHearingItInTheSameHalfIsJoined()
    {
        CatalogSeries renamed = new(1, "まるで違う架空の名前", Evening);
        PlacedOccurrence[] anotherSlot = [.. Weekly(Drama, 2, 0, 89), At(Entry, Quiz.Episode(2), 0, 89)];
        PlacedOccurrence[] otherHalf = [.. Weekly(Drama, 2, 0, 89), At(Entry, renamed.Episode(2), 1700, 89)];
        PlacedOccurrence[] offTheGrid = [.. Weekly(Drama, 2, 0, 85), At(Entry, Quiz.Episode(2), 0, 85)];

        Assert.Equal(new CatalogEstimate(CatalogKind.Cm, 0.9, CatalogDecision.Pattern), Estimated(89, anotherSlot).Estimate);
        Assert.Equal(new CatalogEstimate(CatalogKind.Cm, 0.9, CatalogDecision.Pattern), Estimated(89, otherHalf).Estimate);
        Assert.Equal((CatalogKind.Opening, 2), (Estimated(85, offTheGrid).Estimate.Kind, Estimated(85, offTheGrid).SeriesCount));
    }

    [Fact(DisplayName = "BR-CD-005: a sound of 90 seconds on the grid heard in the first half of three episodes of one series on two stations is its opening")]
    public void AnOpeningOnTheGridOnTwoStationsIsTheOpening()
    {
        PlacedOccurrence[] heard = [.. Weekly(Drama, 2, 0, 90), At(Entry, Elsewhere.Episode(2), 0, 90)];

        CatalogPattern pattern = Estimated(90, heard);

        Assert.Equal(new CatalogEstimate(CatalogKind.Opening, 0.8, CatalogDecision.Pattern), pattern.Estimate);
        Assert.Equal((1, 2), (pattern.SeriesCount, pattern.StationCount));
        Assert.All(pattern.Overrides, Assert.Null);
    }

    [Fact(DisplayName = "BR-CD-005: the opening of one series on two stations heard in part inside an unrelated series' break stays the opening, and that stretch is an advert")]
    public void AnOpeningOnTwoStationsWithAPromotionStaysTheOpening()
    {
        PlacedOccurrence promoted = At(Entry, Quiz.Episode(1), 600, 29);

        CatalogPattern pattern = Estimated(90, [.. Weekly(Drama, 2, 0, 90), At(Entry, Elsewhere.Episode(2), 0, 90), promoted]);

        Assert.Equal(new CatalogEstimate(CatalogKind.Opening, 0.8, CatalogDecision.Pattern), pattern.Estimate);
        Assert.Equal((1, 2), (pattern.SeriesCount, pattern.StationCount));
        Assert.Equal([null, null, null, SegmentKind.Break], pattern.Overrides);
    }

    [Fact(DisplayName = "a sound of 90 seconds on the grid heard in the first half of episodes of two different series is an advert, 0.9")]
    public void ASoundOnTheGridInTwoSeriesIsStillAnAdvert()
    {
        CatalogPattern pattern = Estimated(90, [.. Weekly(Drama, 2, 0, 90), .. Weekly(Quiz, 2, 0, 90, firstWeek: 2)]);

        Assert.Equal(new CatalogEstimate(CatalogKind.Cm, 0.9, CatalogDecision.Pattern), pattern.Estimate);
        Assert.Equal(2, pattern.SeriesCount);
    }

    [Fact(DisplayName = "the same series on another station hears the opening there, off the grid and on it")]
    public void TheSeriesOnAnotherStationIsJoinedByItsName()
    {
        PlacedOccurrence[] offTheGrid = [.. Weekly(Drama, 2, 0, 85), .. Weekly(Elsewhere, 2, 0, 85, firstWeek: 1)];
        PlacedOccurrence[] onTheGrid = [.. Weekly(Drama, 2, 0, 89), .. Weekly(Elsewhere, 2, 0, 89, firstWeek: 1)];

        CatalogPattern off = Estimated(85, [.. offTheGrid, At(Entry, Elsewhere.Episode(3), 1700, 85)]);
        CatalogPattern on = Estimated(89, onTheGrid);

        Assert.Equal(new CatalogEstimate(CatalogKind.Opening, 0.8, CatalogDecision.Pattern), off.Estimate);
        Assert.Equal((1, 2), (off.SeriesCount, off.StationCount));
        Assert.Equal(SegmentKind.Ending, off.Overrides[^1]);
        Assert.Equal(new CatalogEstimate(CatalogKind.Opening, 0.8, CatalogDecision.Pattern), on.Estimate);
    }

    [Fact(DisplayName = "BR-CD-005: a sound of 90 seconds on the grid in the first half of three episodes on two stations and at the end of the last is the opening, and the last stretch its ending")]
    public void AnOpeningOnTwoStationsSungAtTheEndOfTheLastEpisodeStaysTheOpening()
    {
        PlacedOccurrence last = At(Entry, Elsewhere.Episode(3), 1710, 90);

        CatalogPattern pattern = Estimated(90, [.. Weekly(Drama, 2, 0, 90), At(Entry, Elsewhere.Episode(2), 0, 90), last]);

        Assert.Equal(new CatalogEstimate(CatalogKind.Opening, 0.8, CatalogDecision.Pattern), pattern.Estimate);
        Assert.Equal((1, 2), (pattern.SeriesCount, pattern.StationCount));
        Assert.Equal([null, null, null, SegmentKind.Ending], pattern.Overrides);
    }

    [Fact(DisplayName = "BR-CD-011: recordings that share an opening count as one series for another entry")]
    public void OpeningSoundJoinsSeriesForAnotherEntry()
    {
        CatalogSeries renamed = new(1, "まるで違う架空の名前", Evening);
        CatalogEntryId opening = CatalogEntryId.New();
        PlacedOccurrence first = At(Entry, Drama.Episode(0), 600, 15);
        PlacedOccurrence second = At(Entry, renamed.Episode(1), 900, 15);
        CatalogOccurrence[] theme =
        [
            At(opening, Drama.Episode(0), 0, 89, recording: first.Occurrence.RecordingId).Occurrence,
            At(opening, renamed.Episode(1), 0, 89, recording: second.Occurrence.RecordingId).Occurrence,
        ];
        SeriesLinks links = SeriesLinks.SharingSound(
        [
            [.. theme.Where(stretch => CatalogKinds.LinksSeries(CatalogKind.Opening, TimeSpan.FromSeconds(89), stretch)).Select(stretch => stretch.RecordingId)],
        ]);

        Assert.Equal(0.9, CatalogKinds.Estimate(TimeSpan.FromSeconds(15), [first, second], SeriesLinks.None).Estimate.Confidence);
        Assert.Equal(0.5, CatalogKinds.Estimate(TimeSpan.FromSeconds(15), [first, second], links).Estimate.Confidence);
    }

    [Fact(DisplayName = "BR-CD-011: a stretch links series when its entry is an opening, an ending or a programme fixture, it is whole, and it is not an advert on its own")]
    public void WhichStretchesLinkSeries()
    {
        CatalogOccurrence whole = At(Entry, Drama.Episode(0), 0, 89).Occurrence;
        CatalogOccurrence part = At(Entry, Quiz.Episode(0), 600, 15).Occurrence;
        CatalogOccurrence advertised = At(Entry, Quiz.Episode(1), 0, 89).Occurrence;
        advertised.Override(SegmentKind.Break);
        TimeSpan length = TimeSpan.FromSeconds(89);

        Assert.True(CatalogKinds.LinksSeries(CatalogKind.Opening, length, whole));
        Assert.True(CatalogKinds.LinksSeries(CatalogKind.Ending, length, whole));
        Assert.True(CatalogKinds.LinksSeries(CatalogKind.ProgrammeFixture, length, whole));
        Assert.False(CatalogKinds.LinksSeries(CatalogKind.Opening, length, part));
        Assert.False(CatalogKinds.LinksSeries(CatalogKind.Opening, length, advertised));
        Assert.False(CatalogKinds.LinksSeries(CatalogKind.Cm, length, whole));
        Assert.False(CatalogKinds.LinksSeries(CatalogKind.StationFixture, length, whole));
        Assert.False(CatalogKinds.LinksSeries(CatalogKind.Unknown, length, whole));
    }

    [Fact(DisplayName = "BR-CD-011: a series of a name that only begins with the series' own on another station is not joined by the sound, and a sound on the grid there is an advert")]
    public void APrefixNameOnAnotherStationIsNotJoined()
    {
        CatalogSeries longer = new(2, "架空の物語二", Evening);

        CatalogPattern pattern = Estimated(90, [.. Weekly(Drama, 2, 0, 90), At(Entry, longer.Episode(2), 0, 90)]);

        Assert.Equal(new CatalogEstimate(CatalogKind.Cm, 0.9, CatalogDecision.Pattern), pattern.Estimate);
        Assert.Equal(2, pattern.SeriesCount);
    }

    [Fact(DisplayName = "in a programme of five minutes every stretch is near both its start and its end, and the half it is heard in tells the opening from the ending")]
    public void AShortProgrammeTellsByTheHalf()
    {
        CatalogSeries brief = new(1, "架空の小品", Evening, TimeSpan.FromMinutes(5));

        Assert.Equal(new CatalogEstimate(CatalogKind.Opening, 0.8, CatalogDecision.Pattern), Estimated(31, Weekly(brief, 2, 0, 31)).Estimate);
        Assert.Equal(new CatalogEstimate(CatalogKind.Ending, 0.8, CatalogDecision.Pattern), Estimated(31, Weekly(brief, 2, 240, 31)).Estimate);
    }

    [Theory(DisplayName = "a piece starting more than six minutes after the guide's start or 15 seconds before it, or ending more than six minutes before the guide's end or 15 seconds after it, is neither an opening nor an ending")]
    [InlineData(361.0)]
    [InlineData(-16.0)]
    [InlineData(1800.0 - 361 - 85)]
    [InlineData(1800.0 + 16 - 85)]
    public void APieceJustOutsideTheEdgesIsNoTheme(double fromProgrammeStart)
    {
        Assert.Equal(CatalogEstimate.Unknown, Estimated(85, Weekly(Drama, 2, fromProgrammeStart, 85)).Estimate);
    }

    [Fact(DisplayName = "BR-CD-011: a programme of another name in the same time of day on another day of the week, or an hour later the same night, is not joined by the sound")]
    public void AProgrammeInTheBlockOnAnotherDayOrLaterThatNightIsNotJoined()
    {
        CatalogSeries anotherDay = new(1, "まるで違う架空の名前", Evening.AddDays(1));
        CatalogSeries laterThatNight = new(1, "まるで違う架空の名前", Evening.AddMinutes(60));

        Assert.Equal(new CatalogEstimate(CatalogKind.Cm, 0.9, CatalogDecision.Pattern), Estimated(89, [.. Weekly(Drama, 2, 0, 89), At(Entry, anotherDay.Episode(1), 0, 89)]).Estimate);
        Assert.Equal(new CatalogEstimate(CatalogKind.Cm, 0.9, CatalogDecision.Pattern), Estimated(89, [.. Weekly(Drama, 2, 0, 89), At(Entry, laterThatNight.Episode(1), 0, 89)]).Estimate);
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

    [Fact(DisplayName = "BR-CD-011: a daily programme of ten minutes kept under names that differ only in the subtitle hears its opening and its ending in two episodes of one series, 0.8")]
    public void ADailyProgrammeUnderSubtitledNamesHasItsOpeningAndEnding()
    {
        ProgrammeCopy monday = Daily(0, "架空の巻物の段");
        ProgrammeCopy tuesday = Daily(1, "架空の手裏剣の段");

        CatalogPattern opening = Estimated(80, At(Entry, monday, 11, 80), At(Entry, tuesday, 12, 80));
        CatalogPattern ending = Estimated(62, KeptAt(1, monday, 484, 62), KeptAt(2, tuesday, 485, 62));

        Assert.Equal(new CatalogEstimate(CatalogKind.Opening, 0.8, CatalogDecision.Pattern), opening.Estimate);
        Assert.Equal(1, opening.SeriesCount);
        Assert.Equal(new CatalogEstimate(CatalogKind.Ending, 0.8, CatalogDecision.Pattern), ending.Estimate);
        Assert.Equal(1, ending.SeriesCount);
    }

    [Fact(DisplayName = "BR-CD-005: a sound heard only in a broadcast recorded twice is heard in one episode, so it is neither an ending nor a programme fixture")]
    public void ASoundOfABroadcastRecordedTwiceIsHeardInOneEpisode()
    {
        Sound preview = Heard(34, (first, 1700), (again, 1700));
        Sound fixture = Heard(12, (first, 300), (again, 300));
        Sound ending = Heard(34, (first, 1700), (again, 1700), (second, 1700));

        Assert.Equal(CatalogEstimate.Unknown, Own(preview).Estimate);
        Assert.Equal(CatalogEstimate.Unknown, Own(fixture).Estimate);
        Assert.Equal(new CatalogEstimate(CatalogKind.Ending, 0.8, CatalogDecision.Pattern), Own(ending).Estimate);
        Assert.Equal(1, Own(ending).SeriesCount);
    }

    [Fact(DisplayName = "BR-CD-005: the pieces an opening is matched in, heard right after its head at the same place in two episodes, are that opening, 0.8")]
    public void ThePiecesOfAnOpeningAreThatOpening()
    {
        Sound head = Heard(50, Every(0));
        Sound phrase = Heard(13, [.. Every(50), .. Every(600)]);
        Sound slot = Heard(30, Every(63));
        Sound once = Heard(20, (first, 93), (again, 93), (second, 400));
        CatalogSurroundings around = Around(head, phrase, slot, once);

        CatalogPattern phrased = Carried(phrase, around);
        CatalogPattern slotted = Carried(slot, around);

        Assert.Equal(new CatalogEstimate(CatalogKind.Opening, 0.8, CatalogDecision.Pattern), Own(head).Estimate);
        Assert.Equal(new CatalogEstimate(CatalogKind.ProgrammeFixture, 0.7, CatalogDecision.Pattern), Own(phrase).Estimate);
        Assert.Equal(new CatalogEstimate(CatalogKind.Cm, 0.5, CatalogDecision.Pattern), Own(slot).Estimate);
        Assert.Equal(new CatalogEstimate(CatalogKind.Opening, 0.8, CatalogDecision.Continuation), phrased.Estimate);
        Assert.Equal([null, null, null, SegmentKind.Programme, SegmentKind.Programme, SegmentKind.Programme], phrased.Overrides);
        Assert.Equal(new CatalogEstimate(CatalogKind.Opening, 0.8, CatalogDecision.Continuation), slotted.Estimate);
        Assert.All(slotted.Overrides, Assert.Null);
        Assert.Equal(Own(once).Estimate, Carried(once, around).Estimate);
    }

    [Fact(DisplayName = "BR-CD-005: a piece is carried on only up to three minutes from the far end of the run, and an advert of two series ends the run")]
    public void ARunEndsAtThreeMinutesAndAtAnAdvertOfTwoSeries()
    {
        Episode quiz = new(Programme(1, "架空のクイズ", LateEvening.AddHours(3), Lead), RecordingId.New());
        Sound head = Heard(89, Every(0));
        Sound[] pieces = [.. Enumerable.Range(0, 4).Select(at => Heard(25, Every(89 + (25 * at))))];
        Sound advert = Heard(15, [.. Every(1500), (quiz, 600)]);
        Sound afterTheAdvert = Heard(20, Every(1515));
        Sound ending = Heard(89, Every(1640));
        Sound leadingUp = Heard(20, Every(1619));
        CatalogSurroundings around = Around([head, .. pieces, advert, afterTheAdvert, ending, leadingUp]);

        Assert.Equal(
            [CatalogKind.Opening, CatalogKind.Opening, CatalogKind.Opening, CatalogKind.ProgrammeFixture],
            pieces.Select(piece => Carried(piece, around).Estimate.Kind));
        Assert.Equal(new CatalogEstimate(CatalogKind.Cm, 0.9, CatalogDecision.Pattern), Carried(advert, around).Estimate);
        Assert.Equal(CatalogKind.ProgrammeFixture, Carried(afterTheAdvert, around).Estimate.Kind);
        Assert.Equal(new CatalogEstimate(CatalogKind.Ending, 0.8, CatalogDecision.Continuation), Carried(leadingUp, around).Estimate);
    }

    [Fact(DisplayName = "BR-CD-005: a piece heard next to the opening in a broadcast recorded twice, and elsewhere in the other episode, is not carried on")]
    public void APieceNextToTheOpeningInOneEpisodeIsNotCarriedOn()
    {
        Sound head = Heard(50, Every(0));
        Sound slot = Heard(30, (first, 50), (again, 50), (second, 900));
        CatalogSurroundings around = Around(head, slot);

        Assert.Equal(new CatalogEstimate(CatalogKind.Cm, 0.5, CatalogDecision.Pattern), Carried(slot, around).Estimate);
        Assert.All(Carried(slot, around).Overrides, Assert.Null);
    }

    [Fact(DisplayName = "BR-CD-005: a piece carried on is a piece of the run for the next one, and an entry carried on is never the head of a run")]
    public void AnEntryCarriedOnIsAPieceAndNeverAHead()
    {
        Sound slot = Heard(30, Every(63));
        Sound next = Heard(13, Every(93));
        CatalogPattern carried = new(new CatalogEstimate(CatalogKind.Opening, 0.8, CatalogDecision.Continuation), 1, 1, [null, null, null]);
        CatalogSurroundings around = new([.. CatalogKinds.Stretches(slot.Placed, carried), .. CatalogKinds.Stretches(next.Placed, Own(next))]);

        Assert.All(CatalogKinds.Stretches(slot.Placed, carried), stretch => Assert.Equal(ThemePart.Piece, stretch.Part));
        Assert.Equal(CatalogKind.ProgrammeFixture, Carried(next, around).Estimate.Kind);
    }

    [Theory(DisplayName = "BR-CD-005: a piece on the grid heard at the same place in two episodes of one series, right before or after an advert in one of them, is a sponsor's advert, an advert of one series, 0.5")]
    [InlineData(14.0, 825.0, true)]
    [InlineData(14.0, 855.0, true)]
    [InlineData(14.0, 855.0, false)]
    [InlineData(29.0, 825.0, true)]
    [InlineData(29.0, 870.0, false)]
    public void APieceOnTheGridBesideAnAdvertIsASponsorsAdvert(double seconds, double advertFrom, bool inTwoSeries)
    {
        Sound sponsor = HeardWeekly(seconds, (0, 840), (1, 845));
        Sound advert = inTwoSeries ? HeardWeekly(14, (0, advertFrom)).And(QuizBeforeVariety.Episode(0), 600) : HeardWeekly(14, (0, advertFrom), (2, 300));
        CatalogSurroundings around = Around(sponsor, advert);

        CatalogPattern settled = Carried(sponsor, around);

        Assert.Equal(new CatalogEstimate(CatalogKind.ProgrammeFixture, 0.7, CatalogDecision.Pattern), Own(sponsor).Estimate);
        Assert.Equal(new CatalogEstimate(CatalogKind.Cm, inTwoSeries ? 0.9 : 0.5, CatalogDecision.Pattern), Own(advert).Estimate);
        Assert.Equal(new CatalogEstimate(CatalogKind.Cm, 0.5, CatalogDecision.Pattern), settled.Estimate);
        Assert.Equal((1, 1), (settled.SeriesCount, settled.StationCount));
        Assert.All(settled.Overrides, Assert.Null);
    }

    [Theory(DisplayName = "BR-CD-005: an advert counts as beside a piece up to two seconds from it, or over it")]
    [InlineData(850.0, CatalogKind.Cm)]
    [InlineData(854.0, CatalogKind.Cm)]
    [InlineData(856.0, CatalogKind.Cm)]
    [InlineData(856.5, CatalogKind.ProgrammeFixture)]
    [InlineData(823.5, CatalogKind.ProgrammeFixture)]
    [InlineData(824.0, CatalogKind.Cm)]
    public void AnAdvertIsBesideUpToTwoSecondsAway(double advertFrom, CatalogKind kind)
    {
        Sound sponsor = HeardWeekly(14, (0, 840), (1, 840));
        Sound advert = HeardWeekly(14, (0, advertFrom)).And(QuizBeforeVariety.Episode(0), 600);

        Assert.Equal(kind, Carried(sponsor, Around(sponsor, advert)).Estimate.Kind);
    }

    [Fact(DisplayName = "BR-CD-005: a fixed part of the programme off the grid, such as the sponsors read out before the break, stays one beside an advert")]
    public void AFixedPartOffTheGridStaysOneBesideAnAdvert()
    {
        Sound readOut = HeardWeekly(10, (0, 840), (1, 840));
        Sound advert = HeardWeekly(14, (0, 851), (1, 851)).And(QuizBeforeVariety.Episode(0), 600);

        Assert.Equal(new CatalogEstimate(CatalogKind.ProgrammeFixture, 0.7, CatalogDecision.Pattern), Carried(readOut, Around(readOut, advert)).Estimate);
    }

    [Fact(DisplayName = "BR-CD-005: a piece on the grid at the same place stays a fixed part of the programme when nothing beside it counts as a break: another such piece, the programme's own sound, or nothing heard at all")]
    public void APieceOnTheGridBesideNoAdvertStaysAFixedPart()
    {
        Sound sponsor = HeardWeekly(14, (0, 840), (1, 840));
        Sound other = HeardWeekly(14, (0, 855), (1, 855));
        Sound jingle = HeardWeekly(6, (0, 832), (1, 832));

        Assert.Equal(CatalogKind.ProgrammeFixture, Own(other).Estimate.Kind);
        Assert.Equal(CatalogKind.ProgrammeFixture, Carried(sponsor, Around(sponsor, other, jingle)).Estimate.Kind);
        Assert.Equal(CatalogKind.ProgrammeFixture, Carried(sponsor, Around(sponsor)).Estimate.Kind);
    }

    [Fact(DisplayName = "BR-CD-005: an advert heard beside the piece in another recording does not make it a sponsor's advert")]
    public void AnAdvertInAnotherRecordingIsNotBeside()
    {
        Sound sponsor = HeardWeekly(14, (0, 840), (1, 840));
        Sound elsewhere = HeardWeekly(14, (2, 855)).And(QuizBeforeVariety.Episode(0), 600);

        Assert.Equal(CatalogKind.ProgrammeFixture, Carried(sponsor, Around(sponsor, elsewhere)).Estimate.Kind);
    }

    [Fact(DisplayName = "BR-CD-005: a stretch of a sponsor's advert counts as a break beside the next piece, so a run of them is taken in from the advert it leans on")]
    public void ASponsorsAdvertCountsAsABreakBesideTheNext()
    {
        Sound advert = HeardWeekly(14, (0, 825)).And(QuizBeforeVariety.Episode(0), 600);
        Sound firstAfter = HeardWeekly(14, (0, 840), (1, 840));
        Sound secondAfter = HeardWeekly(14, (0, 855), (1, 855));
        CatalogPattern settledFirst = Carried(firstAfter, Around(advert, firstAfter, secondAfter));
        CatalogSurroundings afterwards = new(
        [
            .. CatalogKinds.Stretches(advert.Placed, Own(advert)),
            .. CatalogKinds.Stretches(firstAfter.Placed, settledFirst),
            .. CatalogKinds.Stretches(secondAfter.Placed, Own(secondAfter)),
        ]);

        Assert.Equal(CatalogKind.Cm, settledFirst.Estimate.Kind);
        Assert.Equal(CatalogKind.ProgrammeFixture, Carried(secondAfter, Around(advert, firstAfter, secondAfter)).Estimate.Kind);
        Assert.Equal(new CatalogEstimate(CatalogKind.Cm, 0.5, CatalogDecision.Pattern), Carried(secondAfter, afterwards).Estimate);
        Assert.All(CatalogKinds.Stretches(firstAfter.Placed, settledFirst), stretch => Assert.Equal(SegmentKind.Break, stretch.CountsAs));
    }

    private static ProgrammeCopy Daily(int day, string subtitle)
    {
        DateTime starts = Evening.AddDays(day);
        string keptBefore = $"アニメ 架空の忍者「{subtitle}」";

        return new ProgrammeCopy(new NetworkId(3), new ServiceId(3), starts, starts.AddMinutes(10), starts - Lead, $"{keptBefore}[字]", [], [], null, AudioMode.Stereo, keptBefore);
    }

    private static PlacedOccurrence KeptAt(long id, ProgrammeCopy programme, double fromProgrammeStart, double seconds)
    {
        TimeSpan starts = Lead + TimeSpan.FromSeconds(fromProgrammeStart);
        CatalogOccurrence kept = CatalogOccurrence.Rehydrate(
            id,
            Entry,
            RecordingId.New(),
            starts,
            starts + TimeSpan.FromSeconds(seconds),
            programme.NetworkId,
            programme.ServiceId,
            programme.SeriesName,
            programme.RecordingStartedAt,
            0.05f,
            null,
            Now);

        return PlacedOccurrence.Of(kept, programme);
    }

    private static CatalogPattern Estimated(double seconds, params PlacedOccurrence[] occurrences)
        => CatalogKinds.Estimate(TimeSpan.FromSeconds(seconds), occurrences, SeriesLinks.None);

    private static PlacedOccurrence[] Weekly(CatalogSeries series, int weeks, double fromProgrammeStart, double seconds, int firstWeek = 0)
        => [.. Enumerable.Range(firstWeek, weeks).Select(week => At(Entry, series.Episode(week), fromProgrammeStart, seconds))];

    private static ProgrammeCopy Programme(int station, string? series, DateTime starts, TimeSpan lead)
        => new(
            new NetworkId(station),
            new ServiceId(station),
            starts,
            starts + Runs,
            starts - lead,
            series ?? "架空の物語🈑",
            [],
            [],
            null,
            AudioMode.Stereo,
            series);

    private static PlacedOccurrence Stretch(CatalogEntryId entry, ProgrammeCopy programme, RecordingId recording, double fromProgrammeStart, double seconds)
    {
        TimeSpan starts = programme.ProgrammeStartsAt - programme.RecordingStartedAt + TimeSpan.FromSeconds(fromProgrammeStart);
        SoundSpan span = new(starts, starts + TimeSpan.FromSeconds(seconds));

        return PlacedOccurrence.Of(CatalogOccurrence.Found(entry, recording, span, programme, 0.05, Now), programme);
    }

    private static CatalogPattern Own(Sound sound) => CatalogKinds.Estimate(sound.Length, sound.Placed, SeriesLinks.None);

    private static CatalogPattern Carried(Sound sound, CatalogSurroundings around)
        => CatalogKinds.Estimate(sound.Length, sound.Placed, SeriesLinks.None, around);

    private static CatalogSurroundings Around(params Sound[] sounds)
        => new([.. sounds.SelectMany(sound => CatalogKinds.Stretches(sound.Placed, Own(sound)))]);

    private (Episode Episode, double FromProgrammeStart)[] Every(double fromProgrammeStart)
        => [.. Weeks.Select(episode => (episode, fromProgrammeStart))];

    private static Sound Heard(double seconds, params (Episode Episode, double FromProgrammeStart)[] places)
    {
        CatalogEntryId entry = CatalogEntryId.New();

        return new Sound(
            entry,
            TimeSpan.FromSeconds(seconds),
            [.. places.Select(place => Stretch(entry, place.Episode.Programme, place.Episode.Recording, place.FromProgrammeStart, seconds))]);
    }

    private Sound HeardWeekly(double seconds, params (int Week, double FromProgrammeStart)[] places)
    {
        CatalogEntryId entry = CatalogEntryId.New();

        return new Sound(
            entry,
            TimeSpan.FromSeconds(seconds),
            [.. places.Select(place => CatalogSeries.At(entry, Variety.Episode(place.Week), place.FromProgrammeStart, seconds, recordingsOfTheWeeks[place.Week]))]);
    }

    private sealed record Episode(ProgrammeCopy Programme, RecordingId Recording);

    private sealed record Sound(CatalogEntryId Entry, TimeSpan Length, PlacedOccurrence[] Placed)
    {
        /// <summary>
        /// The sound heard besides in <paramref name="programme"/>, in a recording of its own.
        /// </summary>
        public Sound And(ProgrammeCopy programme, double fromProgrammeStart)
            => this with { Placed = [.. Placed, CatalogSeries.At(Entry, programme, fromProgrammeStart, Length.TotalSeconds)] };
    }
}
