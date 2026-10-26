using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

/// <summary>
/// A synthetic weekly late-night series whose sponsor's advert is put in the same break every week, so that it is
/// heard at about the same place from the start of each episode as a fixed part of the programme is. Each week has a
/// recording of its own, and a quiz on the same station shares the adverts heard in two series.
/// </summary>
public sealed class CatalogSponsorsTests
{
    private static readonly DateTime Night = new(2026, 10, 5, 15, 0, 0, DateTimeKind.Utc);

    private static readonly CatalogSeries Variety = new(1, "架空の深夜番組", Night);

    private static readonly CatalogSeries Quiz = new(1, "架空のクイズ", Night.AddHours(-5));

    private readonly RecordingId[] weeks = [RecordingId.New(), RecordingId.New(), RecordingId.New()];

    [Theory(DisplayName = "BR-CD-005: a piece on the grid heard at the same place in two episodes of one series, right before or after an advert in one of them, is a sponsor's advert, an advert of one series, 0.5")]
    [InlineData(14.0, 825.0, true)]
    [InlineData(14.0, 855.0, true)]
    [InlineData(14.0, 855.0, false)]
    [InlineData(29.0, 825.0, true)]
    [InlineData(29.0, 870.0, false)]
    public void APieceOnTheGridBesideAnAdvertIsASponsorsAdvert(double seconds, double advertFrom, bool inTwoSeries)
    {
        Sound sponsor = Heard(seconds, (0, 840), (1, 845));
        Sound advert = inTwoSeries ? Heard(14, (0, advertFrom)).And(Quiz.Episode(0), 600) : Heard(14, (0, advertFrom), (2, 300));
        CatalogSurroundings around = Around(sponsor, advert);

        CatalogPattern settled = Settled(sponsor, around);

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
        Sound sponsor = Heard(14, (0, 840), (1, 840));
        Sound advert = Heard(14, (0, advertFrom)).And(Quiz.Episode(0), 600);

        Assert.Equal(kind, Settled(sponsor, Around(sponsor, advert)).Estimate.Kind);
    }

    [Fact(DisplayName = "BR-CD-005: a fixed part of the programme off the grid, such as the sponsors read out before the break, stays one beside an advert")]
    public void AFixedPartOffTheGridStaysOneBesideAnAdvert()
    {
        Sound readOut = Heard(10, (0, 840), (1, 840));
        Sound advert = Heard(14, (0, 851), (1, 851)).And(Quiz.Episode(0), 600);

        Assert.Equal(new CatalogEstimate(CatalogKind.ProgrammeFixture, 0.7, CatalogDecision.Pattern), Settled(readOut, Around(readOut, advert)).Estimate);
    }

    [Fact(DisplayName = "BR-CD-005: a piece on the grid at the same place stays a fixed part of the programme when nothing beside it counts as a break: another such piece, the programme's own sound, or nothing heard at all")]
    public void APieceOnTheGridBesideNoAdvertStaysAFixedPart()
    {
        Sound sponsor = Heard(14, (0, 840), (1, 840));
        Sound other = Heard(14, (0, 855), (1, 855));
        Sound jingle = Heard(6, (0, 832), (1, 832));

        Assert.Equal(CatalogKind.ProgrammeFixture, Own(other).Estimate.Kind);
        Assert.Equal(CatalogKind.ProgrammeFixture, Settled(sponsor, Around(sponsor, other, jingle)).Estimate.Kind);
        Assert.Equal(CatalogKind.ProgrammeFixture, Settled(sponsor, Around(sponsor)).Estimate.Kind);
    }

    [Fact(DisplayName = "BR-CD-005: an advert heard beside the piece in another recording does not make it a sponsor's advert")]
    public void AnAdvertInAnotherRecordingIsNotBeside()
    {
        Sound sponsor = Heard(14, (0, 840), (1, 840));
        Sound elsewhere = Heard(14, (2, 855)).And(Quiz.Episode(0), 600);

        Assert.Equal(CatalogKind.ProgrammeFixture, Settled(sponsor, Around(sponsor, elsewhere)).Estimate.Kind);
    }

    [Fact(DisplayName = "BR-CD-005: a stretch of a sponsor's advert counts as a break beside the next piece, so a run of them is taken in from the advert it leans on")]
    public void ASponsorsAdvertCountsAsABreakBesideTheNext()
    {
        Sound advert = Heard(14, (0, 825)).And(Quiz.Episode(0), 600);
        Sound first = Heard(14, (0, 840), (1, 840));
        Sound second = Heard(14, (0, 855), (1, 855));
        CatalogPattern settledFirst = Settled(first, Around(advert, first, second));
        CatalogSurroundings afterwards = new(
        [
            .. CatalogKinds.Stretches(advert.Placed, Own(advert)),
            .. CatalogKinds.Stretches(first.Placed, settledFirst),
            .. CatalogKinds.Stretches(second.Placed, Own(second)),
        ]);

        Assert.Equal(CatalogKind.Cm, settledFirst.Estimate.Kind);
        Assert.Equal(CatalogKind.ProgrammeFixture, Settled(second, Around(advert, first, second)).Estimate.Kind);
        Assert.Equal(new CatalogEstimate(CatalogKind.Cm, 0.5, CatalogDecision.Pattern), Settled(second, afterwards).Estimate);
        Assert.All(CatalogKinds.Stretches(first.Placed, settledFirst), stretch => Assert.Equal(SegmentKind.Break, stretch.CountsAs));
    }

    private static CatalogPattern Own(Sound sound) => CatalogKinds.Estimate(sound.Length, sound.Placed, SeriesLinks.None);

    private static CatalogPattern Settled(Sound sound, CatalogSurroundings around)
        => CatalogKinds.Estimate(sound.Length, sound.Placed, SeriesLinks.None, around);

    private static CatalogSurroundings Around(params Sound[] sounds)
        => new([.. sounds.SelectMany(sound => CatalogKinds.Stretches(sound.Placed, Own(sound)))]);

    private Sound Heard(double seconds, params (int Week, double FromProgrammeStart)[] places)
    {
        CatalogEntryId entry = CatalogEntryId.New();

        return new Sound(
            entry,
            TimeSpan.FromSeconds(seconds),
            [.. places.Select(place => CatalogSeries.At(entry, Variety.Episode(place.Week), place.FromProgrammeStart, seconds, weeks[place.Week]))]);
    }

    private sealed record Sound(CatalogEntryId Entry, TimeSpan Length, PlacedOccurrence[] Placed)
    {
        /// <summary>
        /// The sound heard besides in <paramref name="programme"/>, in a recording of its own.
        /// </summary>
        public Sound And(ProgrammeCopy programme, double fromProgrammeStart)
            => this with { Placed = [.. Placed, CatalogSeries.At(Entry, programme, fromProgrammeStart, Length.TotalSeconds)] };
    }
}
