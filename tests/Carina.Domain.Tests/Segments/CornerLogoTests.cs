using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using static Carina.Domain.Tests.Segments.SyntheticCorners;

namespace Carina.Domain.Tests.Segments;

public sealed class CornerLogoTests
{
    private const int Seconds = 600;

    private static readonly DateTime Evening = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    private static readonly SeriesMember Judged = Member(1, Evening);

    [Fact(DisplayName = "a logo is learned from two earlier recordings of the station and seen in the seconds it is on screen")]
    public void ALogoIsLearnedFromEarlierRecordingsOfTheStation()
    {
        CornerLogo logo = CornerLogo.Learn(Judged, [Recorded(Member(1, Evening.AddDays(-7)), TopRight, 1), Recorded(Member(1, Evening.AddDays(-1)), TopRight, 2)]);
        bool[] seen = logo.Sightings(Drawn(Seconds, TopRight, second => second >= 300, 9));

        Assert.Single(logo.Shapes);
        Assert.Equal(Seconds, seen.Length);
        Assert.All(seen[..300], Assert.False);
        Assert.All(seen[300..], Assert.True);
    }

    [Fact(DisplayName = "a recording never learns its logo from itself, even when handed over among the others")]
    public void ARecordingNeverLearnsFromItself()
    {
        RecordedCorners itself = new(Judged, Drawn(Seconds, TopLeft, _ => true, 3));
        RecordedCorners earlier = Recorded(Member(1, Evening.AddDays(-7)), TopRight, 1);

        CornerLogo alone = CornerLogo.Learn(Judged, [itself, earlier]);
        CornerLogo taught = CornerLogo.Learn(Judged, [itself, itself, earlier, Recorded(Member(1, Evening.AddDays(-14)), TopRight, 2)]);

        Assert.Empty(alone.Shapes);
        Assert.Single(taught.Shapes);
        Assert.All(taught.Sightings(Drawn(Seconds, TopLeft, _ => true, 4)), Assert.False);
        Assert.All(taught.Sightings(Drawn(Seconds, TopRight, _ => true, 4)), Assert.True);
    }

    [Fact(DisplayName = "recordings made later and recordings of other stations teach nothing")]
    public void LaterRecordingsAndOtherStationsTeachNothing()
    {
        CornerLogo logo = CornerLogo.Learn(
            Judged,
            [
                Recorded(Member(1, Evening.AddDays(1)), TopRight, 1),
                Recorded(Member(1, Evening.AddDays(2)), TopRight, 2),
                Recorded(Member(2, Evening.AddDays(-1)), TopRight, 3),
                Recorded(Member(2, Evening.AddDays(-2)), TopRight, 4),
            ]);

        Assert.Empty(logo.Shapes);
        Assert.All(logo.Sightings(Drawn(Seconds, TopRight, _ => true, 5)), Assert.False);
    }

    [Fact(DisplayName = "a station may carry several shapes, each learned from the recordings that showed it")]
    public void AStationMayCarrySeveralShapes()
    {
        CornerLogo logo = CornerLogo.Learn(
            Judged,
            [
                Recorded(Member(1, Evening.AddDays(-60)), TopRight, 1),
                Recorded(Member(1, Evening.AddDays(-53)), TopRight, 2),
                Recorded(Member(1, Evening.AddDays(-7)), TopLeft, 3),
                Recorded(Member(1, Evening.AddDays(-1)), TopLeft, 4),
            ]);

        Assert.Equal(2, logo.Shapes.Count);
        Assert.All(logo.Sightings(Drawn(Seconds, TopRight, _ => true, 5)), Assert.True);
        Assert.All(logo.Sightings(Drawn(Seconds, TopLeft, _ => true, 6)), Assert.True);
        Assert.All(logo.Sightings(Drawn(Seconds, [], _ => false, 7)), Assert.False);
    }

    [Fact(DisplayName = "a steady outline covering too much of the corners, too little of them, or from too short a recording is no logo")]
    public void ASteadyOutlineOfTheWrongSizeIsNoLogo()
    {
        int[] wide = Rectangle(2, 0, 0, 30, 8);
        int[] tiny = Rectangle(3, 10, 10, 1, 2);

        Assert.Empty(CornerLogo.Learn(Judged, [Recorded(Member(1, Evening.AddDays(-2)), wide, 1), Recorded(Member(1, Evening.AddDays(-1)), wide, 2)]).Shapes);
        Assert.Empty(CornerLogo.Learn(Judged, [Recorded(Member(1, Evening.AddDays(-2)), tiny, 1), Recorded(Member(1, Evening.AddDays(-1)), tiny, 2)]).Shapes);
        Assert.Empty(CornerLogo.Learn(
            Judged,
            [
                new RecordedCorners(Member(1, Evening.AddDays(-2)), Drawn(CornerLogo.FewestSeconds - 1, TopRight, _ => true, 1)),
                new RecordedCorners(Member(1, Evening.AddDays(-1)), Drawn(CornerLogo.FewestSeconds - 1, TopRight, _ => true, 2)),
            ]).Shapes);
    }

    [Fact(DisplayName = "the same recordings handed over in another order teach the same shapes")]
    public void TheOrderHandedOverChangesNothing()
    {
        RecordedCorners[] recordings =
        [
            Recorded(Member(1, Evening.AddDays(-60)), TopRight, 1),
            Recorded(Member(1, Evening.AddDays(-53)), TopRight, 2),
            Recorded(Member(1, Evening.AddDays(-7)), TopLeft, 3),
            Recorded(Member(1, Evening.AddDays(-1)), TopLeft, 4),
        ];
        byte[] judged = Drawn(Seconds, TopLeft, second => second % 7 is 0, 8);

        Assert.Equal(CornerLogo.Learn(Judged, recordings).Sightings(judged), CornerLogo.Learn(Judged, recordings.Reverse()).Sightings(judged));
    }

    [Fact(DisplayName = "outlines that are not whole seconds of corners are refused")]
    public void BrokenOutlinesAreRefused()
        => Assert.Throws<ArgumentException>(() => CornerLogo.None.Sightings(new byte[CornerOutline.Bytes + 1]));

    private static RecordedCorners Recorded(SeriesMember member, int[] logo, int seed)
        => new(member, Drawn(Seconds, logo, second => second % 300 >= 60, seed));

    private static SeriesMember Member(int station, DateTime startedAt)
        => new(RecordingId.New(), new NetworkId(station), new ServiceId(station), "架空の物語", startedAt);
}
