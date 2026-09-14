using Carina.Domain.Encodings;

namespace Carina.Domain.Tests.Encodings;

public sealed class EncodeChapterTests
{
    private static readonly EncodeJobId Job = EncodeJobId.New();

    [Fact(DisplayName = "the chapters of a reading are numbered from the first in the order they were read, and each carries the stretch it was read as")]
    public void TheChaptersOfAReadingAreNumberedInTheOrderTheyWereRead()
    {
        IReadOnlyList<EncodeChapter> marked = EncodeChapter.Mark(
            Job,
            [
                new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(30), ChapterKind.Programme),
                new ChapterSegment(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(90), ChapterKind.Break),
                new ChapterSegment(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(130), ChapterKind.Programme),
            ]);

        Assert.Equal([EncodeChapter.FirstOrdinal, 1, 2], marked.Select(chapter => chapter.Ordinal));
        Assert.All(marked, chapter => Assert.Equal(Job, chapter.JobId));
        Assert.Equal(
            [ChapterKind.Programme, ChapterKind.Break, ChapterKind.Programme],
            marked.Select(chapter => chapter.Kind));
        Assert.Equal(TimeSpan.FromSeconds(30), marked[1].StartsAt);
        Assert.Equal(TimeSpan.FromSeconds(90), marked[1].EndsAt);
        Assert.Equal(new ChapterSegment(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(90), ChapterKind.Break), marked[1].Segment);
        Assert.Equal(marked.Count, marked.Select(chapter => chapter.Id).Distinct().Count());
    }

    [Fact]
    public void AChapterEndsAfterItStartsAndNeverBeforeTheArtefactDoes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EncodeChapter.Rehydrate(
            EncodeChapterId.New(), Job, 0, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), ChapterKind.Break));
        Assert.Throws<ArgumentOutOfRangeException>(() => EncodeChapter.Rehydrate(
            EncodeChapterId.New(), Job, 0, TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(30), ChapterKind.Break));
        Assert.Throws<ArgumentOutOfRangeException>(() => EncodeChapter.Rehydrate(
            EncodeChapterId.New(), Job, EncodeChapter.FirstOrdinal - 1, TimeSpan.Zero, TimeSpan.FromSeconds(30), ChapterKind.Break));
        Assert.Throws<ArgumentOutOfRangeException>(() => EncodeChapter.Rehydrate(
            EncodeChapterId.New(), Job, 0, TimeSpan.Zero, TimeSpan.FromSeconds(30), (ChapterKind)7));
    }

    [Fact]
    public void AChapterIdIsNeverEmpty()
        => Assert.Throws<ArgumentException>(() => new EncodeChapterId(Guid.Empty));
}
