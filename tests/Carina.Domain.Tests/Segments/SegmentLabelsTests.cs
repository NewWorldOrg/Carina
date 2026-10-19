using Carina.Domain.Channels;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SegmentLabelsTests
{
    private static readonly DateTime Evening = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    private static readonly SegmentPiece[] Pieces =
    [
        Piece(0, 100),
        Piece(100, 115),
        Piece(115, 130),
        Piece(130, 400),
    ];

    [Theory(DisplayName = "the catalog labels a piece at its entry's confidence, never above nine tenths, and an entry not known or without a confidence labels nothing")]
    [InlineData(SegmentKind.Break, 1.0, 0.9)]
    [InlineData(SegmentKind.Break, 0.5, 0.5)]
    [InlineData(SegmentKind.Programme, 0.7, 0.7)]
    [InlineData(null, 0.6, null)]
    [InlineData(SegmentKind.Break, null, null)]
    public void TheCatalogLabelsAtItsConfidence(SegmentKind? kind, double? confidence, double? weight)
    {
        SegmentContext context = Context([new CatalogHeard(Seconds(100), Seconds(115), kind, 4, 2, 2, confidence)]);

        SegmentLabel? label = SegmentLabels.Of(Pieces, context, [])[1];

        Assert.Equal(weight is double given ? new SegmentLabel(kind!.Value, given, SegmentLabelSource.Catalog) : null, label);
    }

    [Fact(DisplayName = "a skip nobody came back from labels what was skipped at two tenths, and closing the button labels programme at one tenth")]
    public void SkipsAndDismissalsLabelWhatTheViewerSaw()
    {
        IReadOnlyList<SegmentLabel?> labels = SegmentLabels.Of(
            Pieces,
            Context(),
            [Action(ViewerActionKind.Skipped, 100, 115), Action(ViewerActionKind.Dismissed, 115, 130)]);

        Assert.Equal([null, Break(0.2, SegmentLabelSource.Viewer), Programme(0.1, SegmentLabelSource.Viewer), null], labels);
    }

    [Fact(DisplayName = "a skip the viewer came back from labels nothing, and ten seconds from where they came back are programme at two tenths")]
    public void ComingBackLabelsTenSecondsOfProgramme()
    {
        IReadOnlyList<SegmentLabel?> labels = SegmentLabels.Of(
            [Piece(0, 100), Piece(100, 112), Piece(112, 130), Piece(130, 400)],
            Context(),
            [Action(ViewerActionKind.Skipped, 100, 130), Action(ViewerActionKind.Returned, 140, 100, 20)]);

        Assert.Equal([null, Programme(0.2, SegmentLabelSource.Viewer), null, null], labels);
    }

    [Fact(DisplayName = "BR-CD-006: the catalog wins over the viewers, and among viewers the heavier label wins")]
    public void TheCatalogWinsOverViewers()
    {
        SegmentContext context = Context(
        [
            new CatalogHeard(Seconds(0), Seconds(115), SegmentKind.Programme, 3, 1, 1, 0.7),
        ]);
        ViewerAction[] actions =
        [
            Action(ViewerActionKind.Skipped, 0, 130),
            Action(ViewerActionKind.Dismissed, 115, 130),
            Action(ViewerActionKind.Skipped, 130, 400, 1),
            Action(ViewerActionKind.Returned, 401, 130, 2),
        ];

        IReadOnlyList<SegmentLabel?> labels = SegmentLabels.Of(Pieces, context, actions);

        Assert.Equal([Programme(0.7, SegmentLabelSource.Catalog), Programme(0.7, SegmentLabelSource.Catalog), Break(0.2, SegmentLabelSource.Viewer), null], labels);
    }

    [Fact(DisplayName = "a skip and a return of equal weight over one piece label it programme")]
    public void ATieAmongViewersGoesToProgramme()
    {
        IReadOnlyList<SegmentLabel?> labels = SegmentLabels.Of(
            [Piece(0, 100), Piece(100, 110), Piece(110, 400)],
            Context(),
            [Action(ViewerActionKind.Skipped, 98, 112, 0), Action(ViewerActionKind.Skipped, 200, 300, 1), Action(ViewerActionKind.Returned, 310, 100, 2)]);

        Assert.Equal([null, Programme(0.2, SegmentLabelSource.Viewer), null], labels);
    }

    [Fact(DisplayName = "a piece more than half outside the guide's programme is never a label, even where the catalog hears something")]
    public void PiecesOutsideTheGuideAreLeftOut()
    {
        SegmentPiece[] pieces = [Piece(0, 30, 1), Piece(30, 50, 0.4), Piece(50, 70, 0.6), Piece(70, 400)];
        SegmentContext context = Context([new CatalogHeard(Seconds(0), Seconds(400), SegmentKind.Programme, 3, 1, 1, 0.7)]);

        IReadOnlyList<SegmentLabel?> labels = SegmentLabels.Of(pieces, context, []);

        Assert.Equal([null, Programme(0.7, SegmentLabelSource.Catalog), null, Programme(0.7, SegmentLabelSource.Catalog)], labels);
    }

    [Fact(DisplayName = "a piece no one kind covers more than half of is no label")]
    public void APieceSplitBetweenKindsIsNoLabel()
    {
        SegmentContext context = Context(
        [
            new CatalogHeard(Seconds(0), Seconds(107.5), SegmentKind.Programme, 3, 1, 1, 0.7),
            new CatalogHeard(Seconds(107.5), Seconds(400), SegmentKind.Break, 3, 2, 2, 0.9),
        ]);

        SegmentLabel? label = SegmentLabels.Of(Pieces, context, [])[1];

        Assert.Null(label);
    }

    [Fact(DisplayName = "BR-CD-006: the catalog labels openings and endings at their confidence, as it does breaks")]
    public void TheCatalogLabelsOpeningsAndEndings()
    {
        SegmentContext context = Context(
        [
            new CatalogHeard(Seconds(130), Seconds(400), SegmentKind.Opening, 3, 1, 1, 0.8),
            new CatalogHeard(Seconds(100), Seconds(130), SegmentKind.Ending, 3, 1, 1, 0.8),
        ]);

        IReadOnlyList<SegmentLabel?> labels = SegmentLabels.Of(Pieces, context, []);

        Assert.Equal(
            [null, new SegmentLabel(SegmentKind.Ending, 0.8, SegmentLabelSource.Catalog), new SegmentLabel(SegmentKind.Ending, 0.8, SegmentLabelSource.Catalog), new SegmentLabel(SegmentKind.Opening, 0.8, SegmentLabelSource.Catalog)],
            labels);
    }

    [Fact(DisplayName = "BR-CD-006: in a recording the catalog is sure of an advert in, a segment wholly inside the guide that no stretch of the catalog or action of a viewer touches is the programme at three tenths, at a half where the logo shows through at least eight tenths of it")]
    public void WhatNothingTouchesIsTheProgramme()
    {
        SegmentPiece[] pieces =
        [
            Piece(0, 60, outside: 0.2),
            Piece(60, 100, logo: 0.8),
            Piece(100, 115, logo: 0.9),
            Piece(115, 130, logo: 0.79),
            Piece(130, 200),
            Piece(200, 260),
            Piece(260, 300),
            Piece(300, 330),
            Piece(330, 400, logo: 1),
        ];
        SegmentContext context = Context([SureAdvert(100, 115), new CatalogHeard(Seconds(220), Seconds(240), null, 2, 1, 1, null)]);
        ViewerAction[] actions = [Action(ViewerActionKind.Dismissed, 260, 300), Action(ViewerActionKind.Skipped, 310, 320)];

        IReadOnlyList<SegmentLabel?> labels = SegmentLabels.Of(pieces, context, actions);

        Assert.Equal(
            [
                null,
                Uncovered(0.5),
                Break(0.9, SegmentLabelSource.Catalog),
                Uncovered(0.3),
                Uncovered(0.3),
                null,
                Programme(0.1, SegmentLabelSource.Viewer),
                null,
                Uncovered(0.5),
            ],
            labels);
        Assert.Equal((0.3, 0.5, 0.8), (SegmentLabels.UncoveredWeight, SegmentLabels.UncoveredWithLogoWeight, SegmentLabels.LogoSeenThrough));
    }

    [Fact(DisplayName = "BR-CD-006: a recording the catalog is not sure of an advert in has no programme by elimination, and neither has a segment whose place in the guide is not known")]
    public void WithoutASureAdvertNothingIsTheProgrammeByElimination()
    {
        SegmentPiece[] pieces = [Piece(0, 100, logo: 1), Piece(100, 115), Piece(115, 400, outside: null)];

        IReadOnlyList<SegmentLabel?> unsure = SegmentLabels.Of(pieces, Context([new CatalogHeard(Seconds(100), Seconds(115), SegmentKind.Break, 2, 1, 1, 0.5)]), []);
        IReadOnlyList<SegmentLabel?> sure = SegmentLabels.Of(pieces, Context([SureAdvert(100, 115)]), []);

        Assert.Equal([null, Break(0.5, SegmentLabelSource.Catalog), null], unsure);
        Assert.Equal([Uncovered(0.5), Break(0.9, SegmentLabelSource.Catalog), null], sure);
    }

    [Fact(DisplayName = "BR-CD-008: a stretch set aside from what a recording hears still keeps the segment it lies over from being the programme, and with every advert the catalog is sure of set aside nothing is the programme by elimination")]
    public void AStretchSetAsideStillTouchesItsSegment()
    {
        SegmentPiece[] pieces = [Piece(0, 100), Piece(100, 115), Piece(115, 300), Piece(300, 315), Piece(315, 400)];
        CatalogHeard setAside = SureAdvert(100, 115);
        CatalogHeard kept = SureAdvert(300, 315);

        IReadOnlyList<SegmentLabel?> oneLeft = SegmentLabels.Of(pieces, Context([kept]), [], [setAside, kept]);
        IReadOnlyList<SegmentLabel?> noneLeft = SegmentLabels.Of(pieces, Context([]), [], [setAside, kept]);

        Assert.Equal([Uncovered(0.3), null, Uncovered(0.3), Break(0.9, SegmentLabelSource.Catalog), Uncovered(0.3)], oneLeft);
        Assert.All(noneLeft, Assert.Null);
    }

    [Fact(DisplayName = "a viewer's action is on a break, an opening or an ending, at no time before the recording's zero")]
    public void AViewersActionIsOnWhatAButtonOffers()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ViewerAction(Evening, ViewerActionKind.Skipped, SegmentKind.Programme, Seconds(0), Seconds(10), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ViewerAction(Evening, ViewerActionKind.Skipped, SegmentKind.Break, Seconds(-1), Seconds(10), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ViewerAction(Evening, (ViewerActionKind)9, SegmentKind.Break, Seconds(0), Seconds(10), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ViewerAction(Evening, ViewerActionKind.Skipped, SegmentKind.Break, Seconds(0), Seconds(10), 0));
    }

    private static SegmentLabel Programme(double weight, SegmentLabelSource source) => new(SegmentKind.Programme, weight, source);

    private static SegmentLabel Break(double weight, SegmentLabelSource source) => new(SegmentKind.Break, weight, source);

    private static ViewerAction Action(ViewerActionKind action, double from, double to, int minutes = 0)
        => new(Evening.AddMinutes(minutes), action, SegmentKind.Break, Seconds(from), Seconds(to), 1);

    private static SegmentPiece Piece(double from, double to, double? outside = 0, double? logo = null)
        => new(
            Seconds(from),
            Seconds(to),
            Seconds(to - from),
            SegmentFeatureValues.Of(new Dictionary<string, double?> { [SegmentFeature.OutsideGuide] = outside, [SegmentFeature.LogoShare] = logo }));

    private static CatalogHeard SureAdvert(double from, double to) => new(Seconds(from), Seconds(to), SegmentKind.Break, 3, 2, 2, 0.9);

    private static SegmentLabel Uncovered(double weight) => new(SegmentKind.Programme, weight, SegmentLabelSource.Uncovered);

    private static SegmentContext Context(IReadOnlyList<CatalogHeard>? catalog = null)
        => new(new NetworkId(1), new ServiceId(1), "架空の物語", null, null, [], catalog ?? [], CornerLogo.None, []);

    private static TimeSpan Seconds(double seconds) => TimeSpan.FromSeconds(seconds);
}
