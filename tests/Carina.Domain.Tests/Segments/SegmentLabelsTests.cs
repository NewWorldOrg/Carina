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

    [Fact(DisplayName = "a confirmed answer labels every piece with the kind covering most of it, at a weight of one")]
    public void AConfirmedAnswerLabelsEveryPiece()
    {
        GivenAnswer answer = Answer(true, (0, 101, SegmentKind.Programme), (101, 130, SegmentKind.Break), (130, 400, SegmentKind.Programme));

        IReadOnlyList<SegmentLabel?> labels = SegmentLabels.Of(Pieces, Context(), answer, []);

        Assert.Equal(
            [Programme(1.0, SegmentLabelSource.Answer), Break(1.0, SegmentLabelSource.Answer), Break(1.0, SegmentLabelSource.Answer), Programme(1.0, SegmentLabelSource.Answer)],
            labels);
    }

    [Fact(DisplayName = "an answer not confirmed labels only the pieces it changed from the detection, and the catalog labels the rest")]
    public void AnUnconfirmedAnswerLabelsOnlyWhatItChanged()
    {
        GivenAnswer answer = Answer(
            false,
            [(0, 100, SegmentKind.Programme), (100, 130, SegmentKind.Break), (130, 400, SegmentKind.Programme)],
            [(0, 115, SegmentKind.Programme), (115, 130, SegmentKind.Break), (130, 400, SegmentKind.Programme)]);
        SegmentContext context = Context([new CatalogHeard(Seconds(0), Seconds(100), SegmentKind.Programme, 3, 1, 1, 0.7)]);

        IReadOnlyList<SegmentLabel?> labels = SegmentLabels.Of(Pieces, context, answer, []);

        Assert.Equal([Programme(0.7, SegmentLabelSource.Catalog), Break(1.0, SegmentLabelSource.Answer), null, null], labels);
    }

    [Fact(DisplayName = "an answer not confirmed with no detection beside it is read against programme throughout")]
    public void AnUnconfirmedAnswerWithoutADetectionIsReadAgainstProgramme()
    {
        GivenAnswer answer = Answer(false, [(0, 100, SegmentKind.Programme), (100, 115, SegmentKind.Break), (115, 400, SegmentKind.Programme)], null);

        IReadOnlyList<SegmentLabel?> labels = SegmentLabels.Of(Pieces, Context(), answer, []);

        Assert.Equal([null, Break(1.0, SegmentLabelSource.Answer), null, null], labels);
    }

    [Fact(DisplayName = "a withdrawn answer labels nothing, and what the catalog and the viewers say stands")]
    public void AWithdrawnAnswerLabelsNothing()
    {
        GivenAnswer given = Answer(true, (0, 100, SegmentKind.Programme), (100, 130, SegmentKind.Break), (130, 400, SegmentKind.Programme));
        GivenAnswer answer = new(given.Segments, given.Confirmed, given.AnsweredAt, true, null);
        SegmentContext context = Context([new CatalogHeard(Seconds(100), Seconds(115), SegmentKind.Break, 4, 2, 2, 0.9)]);

        IReadOnlyList<SegmentLabel?> labels = SegmentLabels.Of(Pieces, context, answer, [Action(ViewerActionKind.Skipped, 115, 130)]);

        Assert.Equal([null, Break(0.9, SegmentLabelSource.Catalog), Break(0.2, SegmentLabelSource.Viewer), null], labels);
    }

    [Theory(DisplayName = "the catalog labels a piece at its entry's confidence, never above nine tenths, and an entry not known or without a confidence labels nothing")]
    [InlineData(SegmentKind.Break, 1.0, 0.9)]
    [InlineData(SegmentKind.Break, 0.5, 0.5)]
    [InlineData(SegmentKind.Programme, 0.7, 0.7)]
    [InlineData(null, 0.6, null)]
    [InlineData(SegmentKind.Break, null, null)]
    public void TheCatalogLabelsAtItsConfidence(SegmentKind? kind, double? confidence, double? weight)
    {
        SegmentContext context = Context([new CatalogHeard(Seconds(100), Seconds(115), kind, 4, 2, 2, confidence)]);

        SegmentLabel? label = SegmentLabels.Of(Pieces, context, null, [])[1];

        Assert.Equal(weight is double given ? new SegmentLabel(kind!.Value, given, SegmentLabelSource.Catalog) : null, label);
    }

    [Fact(DisplayName = "a skip nobody came back from labels what was skipped at two tenths, and closing the button labels programme at one tenth")]
    public void SkipsAndDismissalsLabelWhatTheViewerSaw()
    {
        IReadOnlyList<SegmentLabel?> labels = SegmentLabels.Of(
            Pieces,
            Context(),
            null,
            [Action(ViewerActionKind.Skipped, 100, 115), Action(ViewerActionKind.Dismissed, 115, 130)]);

        Assert.Equal([null, Break(0.2, SegmentLabelSource.Viewer), Programme(0.1, SegmentLabelSource.Viewer), null], labels);
    }

    [Fact(DisplayName = "a skip the viewer came back from labels nothing, and ten seconds from where they came back are programme at two tenths")]
    public void ComingBackLabelsTenSecondsOfProgramme()
    {
        IReadOnlyList<SegmentLabel?> labels = SegmentLabels.Of(
            [Piece(0, 100), Piece(100, 112), Piece(112, 130), Piece(130, 400)],
            Context(),
            null,
            [Action(ViewerActionKind.Skipped, 100, 130), Action(ViewerActionKind.Returned, 140, 100, 20)]);

        Assert.Equal([null, Programme(0.2, SegmentLabelSource.Viewer), null, null], labels);
    }

    [Fact(DisplayName = "an answer wins over the catalog and the catalog over the viewers, and among viewers the heavier label wins, a tie going to programme")]
    public void AnswersWinOverTheCatalogAndTheCatalogOverViewers()
    {
        GivenAnswer answer = Answer(false, [(0, 100, SegmentKind.Break), (100, 400, SegmentKind.Programme)], null);
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

        IReadOnlyList<SegmentLabel?> labels = SegmentLabels.Of(Pieces, context, answer, actions);

        Assert.Equal([Break(1.0, SegmentLabelSource.Answer), Programme(0.7, SegmentLabelSource.Catalog), Break(0.2, SegmentLabelSource.Viewer), null], labels);
    }

    [Fact(DisplayName = "a skip and a return of equal weight over one piece label it programme")]
    public void ATieAmongViewersGoesToProgramme()
    {
        IReadOnlyList<SegmentLabel?> labels = SegmentLabels.Of(
            [Piece(0, 100), Piece(100, 110), Piece(110, 400)],
            Context(),
            null,
            [Action(ViewerActionKind.Skipped, 98, 112, 0), Action(ViewerActionKind.Skipped, 200, 300, 1), Action(ViewerActionKind.Returned, 310, 100, 2)]);

        Assert.Equal([null, Programme(0.2, SegmentLabelSource.Viewer), null], labels);
    }

    [Fact(DisplayName = "a piece more than half outside the guide's programme is never a label, even under a confirmed answer")]
    public void PiecesOutsideTheGuideAreLeftOut()
    {
        SegmentPiece[] pieces = [Piece(0, 30, 1), Piece(30, 50, 0.4), Piece(50, 70, 0.6), Piece(70, 400)];
        GivenAnswer answer = Answer(true, (0, 400, SegmentKind.Programme));

        IReadOnlyList<SegmentLabel?> labels = SegmentLabels.Of(pieces, Context(), answer, []);

        Assert.Equal([null, Programme(1.0, SegmentLabelSource.Answer), null, Programme(1.0, SegmentLabelSource.Answer)], labels);
    }

    [Fact(DisplayName = "a piece no one kind covers more than half of is no label")]
    public void APieceSplitBetweenKindsIsNoLabel()
    {
        GivenAnswer answer = Answer(true, (0, 107.5, SegmentKind.Programme), (107.5, 400, SegmentKind.Break));

        SegmentLabel? label = SegmentLabels.Of(Pieces, Context(), answer, [])[1];

        Assert.Null(label);
    }

    [Fact(DisplayName = "an answer lays its stretches end to end from the recording's zero")]
    public void AnAnswerIsLaidEndToEnd()
    {
        Assert.Throws<ArgumentException>(() => Answer(true, (1, 400, SegmentKind.Programme)));
        Assert.Throws<ArgumentException>(() => Answer(true, (0, 100, SegmentKind.Programme), (101, 400, SegmentKind.Break)));
        Assert.Throws<ArgumentException>(() => new GivenAnswer([], true, Evening, false, null));
        Assert.Throws<ArgumentException>(() => new GivenAnswer([Span(0, 400, SegmentKind.Programme)], true, DateTime.SpecifyKind(Evening, DateTimeKind.Local), false, null));
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

    private static GivenAnswer Answer(bool confirmed, params (double From, double To, SegmentKind Kind)[] spans)
        => Answer(confirmed, spans, null);

    private static GivenAnswer Answer(bool confirmed, (double From, double To, SegmentKind Kind)[] spans, (double From, double To, SegmentKind Kind)[]? detected)
        => new(
            [.. spans.Select(span => Span(span.From, span.To, span.Kind))],
            confirmed,
            Evening,
            false,
            detected is null ? null : [.. detected.Select(span => Span(span.From, span.To, span.Kind))]);

    private static ViewerAction Action(ViewerActionKind action, double from, double to, int minutes = 0)
        => new(Evening.AddMinutes(minutes), action, SegmentKind.Break, Seconds(from), Seconds(to), 1);

    private static SegmentSpan Span(double from, double to, SegmentKind kind) => new(Seconds(from), Seconds(to), kind, null);

    private static SegmentPiece Piece(double from, double to, double outside = 0)
        => new(Seconds(from), Seconds(to), Seconds(to - from), SegmentFeatureValues.Of(new Dictionary<string, double?> { [SegmentFeature.OutsideGuide] = outside }));

    private static SegmentContext Context(IReadOnlyList<CatalogHeard>? catalog = null)
        => new(new NetworkId(1), new ServiceId(1), "架空の物語", null, null, [], catalog ?? [], CornerLogo.None, []);

    private static TimeSpan Seconds(double seconds) => TimeSpan.FromSeconds(seconds);
}
