using Carina.Domain.Channels;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SegmentSamplesTests
{
    private static readonly DateTime Evening = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "BR-CD-006: an advert cut in two by the logo, read as one, that the catalog covers is learned as a break with the features detection scores it by, and each of its two segments keeps its own label beside it")]
    public void AJoinedAdvertTheCatalogCoversIsABreak()
    {
        SegmentPiece[] pieces = [Piece(0, 100), Piece(100, 112), Piece(112, 130), Piece(130, 400)];
        JoinedPiece advert = new(1, 2, Piece(100, 130, edge: 1, multiple: 1), [SegmentKind.Break]);

        IReadOnlyList<SegmentSample> samples = SegmentSamples.Of(Read(pieces, advert), Context([SureAdvert(100, 130)]), []);

        Assert.Equal(
            [
                (0, SegmentKind.Programme, 0.3),
                (1, SegmentKind.Break, 0.9),
                (2, SegmentKind.Break, 0.9),
                (3, SegmentKind.Programme, 0.3),
                (4, SegmentKind.Break, 0.9),
            ],
            Learned(samples, [.. pieces, advert.Piece]));
        Assert.All(samples, sample => Assert.Equal(("1:1", "架空の物語"), (sample.Station, sample.Series)));
    }

    [Fact(DisplayName = "BR-CD-006: a stretch read as one opening that the catalog's opening covers all of but four seconds is learned as an opening, as each of its segments is, and one reaching further past the opening is not, though the opening covers more than half of it")]
    public void AJoinedOpeningTheCatalogCoversIsAnOpening()
    {
        SegmentPiece[] pieces = [Piece(0, 10), Piece(10, 40), Piece(40, 70), Piece(70, 95), Piece(95, 130), Piece(130, 400)];
        JoinedPiece opening = new(1, 3, Piece(9, 97), [SegmentKind.Opening]);
        JoinedPiece further = new(1, 4, Piece(10, 130), [SegmentKind.Opening]);
        CatalogHeard heard = new(Seconds(11), Seconds(95), SegmentKind.Opening, 3, 1, 1, 0.8);

        IReadOnlyList<SegmentSample> samples = SegmentSamples.Of(Read(pieces, opening, further), Context([heard]), []);

        Assert.Equal(
            [
                (1, SegmentKind.Opening, 0.8),
                (2, SegmentKind.Opening, 0.8),
                (3, SegmentKind.Opening, 0.8),
                (6, SegmentKind.Opening, 0.8),
            ],
            Learned(samples, [.. pieces, opening.Piece, further.Piece]));
        Assert.Equal(TimeSpan.FromSeconds(4), SegmentLabels.MostUncoveredOfJoined);
    }

    [Fact(DisplayName = "BR-CD-006: in a recording the catalog is sure of an advert in, a stretch read as one that neither the catalog nor a viewer covers is never the programme by elimination, shaped like an advert or like an opening, while its segments stay the programme where they are not shaped like an advert")]
    public void AJoinedReadingNothingCoversIsNoProgramme()
    {
        SegmentPiece[] pieces = [Piece(0, 100), Piece(100, 112), Piece(112, 130), Piece(130, 175), Piece(175, 220), Piece(220, 300), Piece(300, 315), Piece(315, 400)];
        JoinedPiece advert = new(1, 2, Piece(100, 130, edge: 1, multiple: 1), [SegmentKind.Break]);
        JoinedPiece opening = new(3, 4, Piece(130, 220, edge: 0, multiple: 0), [SegmentKind.Opening]);

        IReadOnlyList<SegmentSample> samples = SegmentSamples.Of(Read(pieces, advert, opening), Context([SureAdvert(300, 315)]), []);

        Assert.Equal(
            [
                (0, SegmentKind.Programme, 0.3),
                (1, SegmentKind.Programme, 0.3),
                (2, SegmentKind.Programme, 0.3),
                (3, SegmentKind.Programme, 0.3),
                (4, SegmentKind.Programme, 0.3),
                (5, SegmentKind.Programme, 0.3),
                (6, SegmentKind.Break, 0.9),
                (7, SegmentKind.Programme, 0.3),
            ],
            Learned(samples, [.. pieces, advert.Piece, opening.Piece]));
    }

    [Fact(DisplayName = "a skip nobody came back from labels a stretch read as one that it covers more than half of, as it does a segment, though not all of it, since a skip starts where the viewer pressed the button; and a stretch read as one more than half outside the guide's programme is never a label")]
    public void ViewersLabelAJoinedReading()
    {
        SegmentPiece[] pieces = [Piece(0, 300), Piece(300, 340), Piece(340, 390), Piece(390, 400, outside: 1)];
        JoinedPiece ending = new(1, 2, Piece(300, 390), [SegmentKind.Ending]);
        JoinedPiece late = new(2, 3, Piece(340, 400, outside: 0.6), [SegmentKind.Ending]);
        ViewerAction skipped = new(Evening, ViewerActionKind.Skipped, SegmentKind.Ending, Seconds(315), Seconds(400), 1);

        IReadOnlyList<SegmentSample> samples = SegmentSamples.Of(Read(pieces, ending, late), Context(), [skipped]);

        Assert.Equal(
            [
                (1, SegmentKind.Ending, 0.2),
                (2, SegmentKind.Ending, 0.2),
                (4, SegmentKind.Ending, 0.2),
            ],
            Learned(samples, [.. pieces, ending.Piece, late.Piece]));
    }

    [Fact(DisplayName = "BR-CD-008: a stretch read as one over an entry set aside takes no label, and the stretch set aside still keeps the segments it lies over from being the programme")]
    public void AJoinedReadingOverAnEntrySetAsideIsNoLabel()
    {
        SegmentPiece[] pieces = [Piece(0, 100), Piece(100, 112), Piece(112, 130), Piece(130, 300), Piece(300, 315), Piece(315, 400)];
        JoinedPiece advert = new(1, 2, Piece(100, 130, edge: 1, multiple: 1), [SegmentKind.Break]);
        CatalogHeard setAside = SureAdvert(100, 130);
        CatalogHeard kept = SureAdvert(300, 315);

        IReadOnlyList<SegmentSample> samples = SegmentSamples.Of(Read(pieces, advert), Context([kept]), [], [setAside, kept]);

        Assert.Equal(
            [
                (0, SegmentKind.Programme, 0.3),
                (3, SegmentKind.Programme, 0.3),
                (4, SegmentKind.Break, 0.9),
                (5, SegmentKind.Programme, 0.3),
            ],
            Learned(samples, [.. pieces, advert.Piece]));
    }

    private static (int At, SegmentKind Kind, double Weight)[] Learned(IReadOnlyList<SegmentSample> samples, SegmentPiece[] read)
        => [.. samples.Select(sample => (Array.FindIndex(read, piece => ReferenceEquals(piece.Features, sample.Features)), sample.Kind, sample.Weight))];

    private static RecordingPieces Read(SegmentPiece[] pieces, params JoinedPiece[] joined)
        => new(pieces, [.. pieces.Skip(1).Select(piece => piece.Starts)], pieces[^1].Ends, "1:1", "架空の物語") { Joined = joined };

    private static SegmentPiece Piece(double from, double to, double? outside = 0, double? edge = null, double? multiple = null)
        => new(
            Seconds(from),
            Seconds(to),
            Seconds(to - from),
            SegmentFeatureValues.Of(new Dictionary<string, double?>
            {
                [SegmentFeature.Length] = to - from,
                [SegmentFeature.OutsideGuide] = outside,
                [SegmentFeature.EdgeSilence] = edge,
                [SegmentFeature.IsMultipleOf15s] = multiple,
            }));

    private static CatalogHeard SureAdvert(double from, double to) => new(Seconds(from), Seconds(to), SegmentKind.Break, 3, 2, 2, 0.9);

    private static SegmentContext Context(IReadOnlyList<CatalogHeard>? catalog = null)
        => new(new NetworkId(1), new ServiceId(1), "架空の物語", null, null, [], catalog ?? [], CornerLogo.None, []);

    private static TimeSpan Seconds(double seconds) => TimeSpan.FromSeconds(seconds);
}
