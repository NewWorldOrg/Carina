using Carina.Domain.Encodings;

namespace Carina.Domain.Tests.Encodings;

public sealed class ChapterRewriteTests
{
    private static readonly DateTime Noon = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly IReadOnlyList<ChapterSegment> Carried = Chapters((0, 299.5, ChapterKind.Programme), (299.5, 359.5, ChapterKind.Break), (359.5, 1799.5, ChapterKind.Programme));

    [Fact(DisplayName = "BR-CD-010: chapters whose boundaries moved by less than a second are left as they are carried")]
    public void ABoundaryMovedByLessThanASecondIsLeftAlone()
    {
        IReadOnlyList<ChapterSegment> wanted = Chapters((0, 300.4, ChapterKind.Programme), (300.4, 358.6, ChapterKind.Break), (358.6, 1799.5, ChapterKind.Programme));

        Assert.Equal(ChapterRewriteVerdict.AsCarried, ChapterRewrite.Judge(Carried, wanted, null, Noon));
        Assert.Equal(ChapterRewriteVerdict.AsCarried, ChapterRewrite.Judge(Carried, Carried, null, Noon));
    }

    [Fact(DisplayName = "BR-CD-010: a boundary moved by a second or more is written again")]
    public void ABoundaryMovedByASecondIsWrittenAgain()
    {
        IReadOnlyList<ChapterSegment> later = Chapters((0, 300.5, ChapterKind.Programme), (300.5, 359.5, ChapterKind.Break), (359.5, 1799.5, ChapterKind.Programme));
        IReadOnlyList<ChapterSegment> earlier = Chapters((0, 299.5, ChapterKind.Programme), (299.5, 358.5, ChapterKind.Break), (358.5, 1799.5, ChapterKind.Programme));

        Assert.Equal(ChapterRewriteVerdict.Due, ChapterRewrite.Judge(Carried, later, null, Noon));
        Assert.Equal(ChapterRewriteVerdict.Due, ChapterRewrite.Judge(Carried, earlier, null, Noon));
    }

    [Fact(DisplayName = "BR-CD-010: where the first chapter starts and the last ends is not a boundary")]
    public void TheEndsOfTheArtefactAreNotBoundaries()
    {
        IReadOnlyList<ChapterSegment> wanted = Chapters((3, 299.5, ChapterKind.Programme), (299.5, 359.5, ChapterKind.Break), (359.5, 1795, ChapterKind.Programme));

        Assert.Equal(ChapterRewriteVerdict.AsCarried, ChapterRewrite.Judge(Carried, wanted, null, Noon));
    }

    [Fact(DisplayName = "BR-CD-010: more chapters, fewer chapters, none at all, or chapters for an artefact that had none are written again")]
    public void ACountThatChangedIsWrittenAgain()
    {
        IReadOnlyList<ChapterSegment> more = Chapters((0, 299.5, ChapterKind.Programme), (299.5, 359.5, ChapterKind.Break), (359.5, 900, ChapterKind.Programme), (900, 960, ChapterKind.Break), (960, 1799.5, ChapterKind.Programme));

        Assert.Equal(ChapterRewriteVerdict.Due, ChapterRewrite.Judge(Carried, more, null, Noon));
        Assert.Equal(ChapterRewriteVerdict.Due, ChapterRewrite.Judge(more, Carried, null, Noon));
        Assert.Equal(ChapterRewriteVerdict.Due, ChapterRewrite.Judge(Carried, [], null, Noon));
        Assert.Equal(ChapterRewriteVerdict.Due, ChapterRewrite.Judge([], Carried, null, Noon));
        Assert.Equal(ChapterRewriteVerdict.AsCarried, ChapterRewrite.Judge([], [], null, Noon));
    }

    [Fact(DisplayName = "BR-CD-010: a chapter that changed its kind is written again")]
    public void AKindThatChangedIsWrittenAgain()
    {
        IReadOnlyList<ChapterSegment> turned = Chapters((0, 299.5, ChapterKind.Break), (299.5, 359.5, ChapterKind.Programme), (359.5, 1799.5, ChapterKind.Break));

        Assert.Equal(ChapterRewriteVerdict.Due, ChapterRewrite.Judge(Carried, turned, null, Noon));
    }

    [Theory(DisplayName = "BR-CD-010: a break that is now an opening or an ending, or the other way about, is written again where the boundaries stay")]
    [InlineData(ChapterKind.Break, ChapterKind.Opening)]
    [InlineData(ChapterKind.Break, ChapterKind.Ending)]
    [InlineData(ChapterKind.Opening, ChapterKind.Break)]
    [InlineData(ChapterKind.Opening, ChapterKind.Ending)]
    public void ABreakThatIsNowAnOpeningOrAnEndingIsWrittenAgain(ChapterKind carried, ChapterKind wanted)
    {
        IReadOnlyList<ChapterSegment> was = Chapters((0, 299.5, ChapterKind.Programme), (299.5, 359.5, carried), (359.5, 1799.5, ChapterKind.Programme));
        IReadOnlyList<ChapterSegment> now = Chapters((0, 299.5, ChapterKind.Programme), (299.5, 359.5, wanted), (359.5, 1799.5, ChapterKind.Programme));

        Assert.Equal(ChapterRewriteVerdict.Due, ChapterRewrite.Judge(was, now, null, Noon));
        Assert.Equal(ChapterRewriteVerdict.AsCarried, ChapterRewrite.Judge(now, now, null, Noon));
    }

    [Fact(DisplayName = "BR-CD-010: an artefact is written again at most once a day")]
    public void AnArtefactIsWrittenAgainAtMostOnceADay()
    {
        IReadOnlyList<ChapterSegment> moved = Chapters((0, 310, ChapterKind.Programme), (310, 359.5, ChapterKind.Break), (359.5, 1799.5, ChapterKind.Programme));

        Assert.Equal(ChapterRewriteVerdict.RewrittenToday, ChapterRewrite.Judge(Carried, moved, Noon.AddHours(-23).AddMinutes(-59), Noon));
        Assert.Equal(ChapterRewriteVerdict.Due, ChapterRewrite.Judge(Carried, moved, Noon.AddDays(-1), Noon));
        Assert.Equal(ChapterRewriteVerdict.AsCarried, ChapterRewrite.Judge(Carried, Carried, Noon.AddMinutes(-1), Noon));
    }

    private static IReadOnlyList<ChapterSegment> Chapters(params (double Starts, double Ends, ChapterKind Kind)[] chapters)
        => [.. chapters.Select(chapter => new ChapterSegment(TimeSpan.FromSeconds(chapter.Starts), TimeSpan.FromSeconds(chapter.Ends), chapter.Kind))];
}
