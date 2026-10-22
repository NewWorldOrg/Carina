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

    [Fact(DisplayName = "a logo on screen in nearly every second or in nearly none tells programme from break nowhere, and no logo tells nothing")]
    public void ALogoEverywhereOrNowhereTellsNothing()
    {
        CornerLogo logo = CornerLogo.Learn(Judged, [Recorded(Member(1, Evening.AddDays(-7)), TopRight, 1), Recorded(Member(1, Evening.AddDays(-1)), TopRight, 2)]);

        Assert.Null(logo.Telling(Drawn(Seconds, TopRight, second => second >= 29, 3)));
        Assert.Null(logo.Telling(Drawn(Seconds, TopRight, second => second < 30, 3)));
        Assert.Null(CornerLogo.None.Telling(Drawn(Seconds, TopRight, second => second < 300, 3)));
        Assert.Null(logo.Telling([]));
        Assert.Equal(logo.Sightings(Drawn(Seconds, TopRight, second => second >= 31, 3)), logo.Telling(Drawn(Seconds, TopRight, second => second >= 31, 3)));
    }

    [Fact(DisplayName = "a logo inside a still picture covering much of the corners is learned from that recording and one clean recording")]
    public void ALogoInsideAStillPictureIsLearnedWithOneCleanRecording()
    {
        int[] still = [.. Rectangle(0, 1, 1, 28, 15), .. Rectangle(1, 1, 1, 28, 15)];
        RecordedCorners pictured = new(Member(1, Evening.AddDays(-7)), Drawn(Seconds, [.. still, .. TopRight], second => second % 300 >= 60, 1));

        CornerLogo logo = CornerLogo.Learn(Judged, [pictured, Recorded(Member(1, Evening.AddDays(-1)), TopRight, 2)]);

        Assert.Equal(TopRight.Order(), Assert.Single(logo.Shapes));
        Assert.Empty(CornerLogo.Learn(Judged, [pictured]).Shapes);
    }

    [Fact(DisplayName = "still pictures draw no logo among themselves, however much of the corners they share")]
    public void StillPicturesDrawNoLogoAmongThemselves()
    {
        RecordedCorners one = new(Member(1, Evening.AddDays(-14)), Drawn(Seconds, [.. Rectangle(0, 1, 1, 28, 15), .. Rectangle(1, 1, 1, 28, 8)], _ => true, 1));
        RecordedCorners other = new(Member(1, Evening.AddDays(-7)), Drawn(Seconds, [.. Rectangle(1, 1, 4, 28, 12), .. Rectangle(3, 1, 1, 28, 15)], _ => true, 2));
        RecordedCorners again = new(Member(1, Evening.AddDays(-1)), Drawn(Seconds, [.. Rectangle(1, 1, 4, 28, 12), .. Rectangle(3, 1, 1, 28, 15)], _ => true, 3));

        Assert.Empty(CornerLogo.Learn(Judged, [one, other]).Shapes);
        Assert.Empty(CornerLogo.Learn(Judged, [one, other, again]).Shapes);
    }

    [Fact(DisplayName = "a picture busy all over a corner confirms no logo there by chance")]
    public void APictureBusyAllOverACornerConfirmsNoLogoByChance()
    {
        int[] checkered = [.. Enumerable.Range(0, CornerOutline.BlocksInACorner)
            .Where(at => ((at / CornerOutline.Columns) + (at % CornerOutline.Columns)) % 2 is 0)
            .Select(at => CornerOutline.BlocksInACorner + at)];
        RecordedCorners busy = new(Member(1, Evening.AddDays(-7)), Drawn(Seconds, checkered, _ => true, 1));

        Assert.Empty(CornerLogo.Learn(Judged, [busy, Recorded(Member(1, Evening.AddDays(-1)), TopRight, 2)]).Shapes);
    }

    [Fact(DisplayName = "a thin logo on in fewer than half the seconds is learned when two recordings agree on it")]
    public void AThinLogoIsLearnedWhenTwoRecordingsAgreeOnIt()
    {
        CornerLogo logo = CornerLogo.Learn(Judged, [Thin(Member(1, Evening.AddDays(-7)), 1), Thin(Member(1, Evening.AddDays(-1)), 2)]);

        Assert.Equal(TopRight.Order(), Assert.Single(logo.Shapes));
        Assert.Empty(CornerLogo.Learn(Judged, [Thin(Member(1, Evening.AddDays(-1)), 2)]).Shapes);
    }

    [Fact(DisplayName = "two recordings of one broadcast count as one recording")]
    public void TwoRecordingsOfOneBroadcastCountAsOne()
    {
        DateTime broadcast = Evening.AddDays(-7);
        RecordedCorners first = Recorded(Member(1, broadcast, broadcast), TopRight, 1);
        RecordedCorners second = Recorded(Member(1, broadcast.AddSeconds(5), broadcast), TopRight, 2);

        Assert.Empty(CornerLogo.Learn(Judged, [first, second]).Shapes);
        Assert.Single(CornerLogo.Learn(Judged, [first, second, Recorded(Member(1, Evening.AddDays(-1), Evening.AddDays(-1)), TopRight, 3)]).Shapes);
    }

    [Fact(DisplayName = "outlines that are not whole seconds of corners are refused")]
    public void BrokenOutlinesAreRefused()
        => Assert.Throws<ArgumentException>(() => CornerLogo.None.Sightings(new byte[CornerOutline.Bytes + 1]));

    private static RecordedCorners Recorded(SeriesMember member, int[] logo, int seed)
        => new(member, Drawn(Seconds, logo, second => second % 300 >= 60, seed));

    private static RecordedCorners Thin(SeriesMember member, int seed)
        => new(member, Drawn(Seconds, TopRight, second => second % 20 < 9, seed));

    private static SeriesMember Member(int station, DateTime startedAt, DateTime? programmeStartsAt = null)
        => new(RecordingId.New(), new NetworkId(station), new ServiceId(station), "架空の物語", startedAt, programmeStartsAt);
}
