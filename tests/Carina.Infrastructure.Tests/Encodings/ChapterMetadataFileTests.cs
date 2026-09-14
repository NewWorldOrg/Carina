using Carina.Domain.Encodings;
using Carina.Infrastructure.Encodings;
using Carina.Infrastructure.Tests.Integrity;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class ChapterMetadataFileTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly IReadOnlyList<ChapterSegment> ThreeChapters =
    [
        new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(30), ChapterKind.Programme),
        new ChapterSegment(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(90), ChapterKind.Break),
        new ChapterSegment(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(130), ChapterKind.Programme),
    ];

    [Fact(DisplayName = "the chapters of an artefact go down in the one shape ffmpeg reads them from, in whole milliseconds, with the head the encode skips added back on")]
    public void TheChaptersGoDownInTheOneShapeFfmpegReadsThemFrom()
        => Assert.Equal(
            """
            ;FFMETADATA1
            [CHAPTER]
            TIMEBASE=1/1000
            START=500
            END=30500
            title=本編
            [CHAPTER]
            TIMEBASE=1/1000
            START=30500
            END=90500
            title=CM
            [CHAPTER]
            TIMEBASE=1/1000
            START=90500
            END=130500
            title=本編

            """.ReplaceLineEndings("\n"),
            ChapterMetadataFile.Written(ThreeChapters, TimeSpan.FromSeconds(0.5)));

    [Fact(DisplayName = "a run with no head to skip writes the artefact's own clock, so the two agree wherever the skip is nothing")]
    public void ARunWithNoHeadToSkipWritesTheArtefactsOwnClock()
        => Assert.Equal(
            """
            ;FFMETADATA1
            [CHAPTER]
            TIMEBASE=1/1000
            START=0
            END=30000
            title=本編
            [CHAPTER]
            TIMEBASE=1/1000
            START=30000
            END=90000
            title=CM
            [CHAPTER]
            TIMEBASE=1/1000
            START=90000
            END=130000
            title=本編

            """.ReplaceLineEndings("\n"),
            ChapterMetadataFile.Written(ThreeChapters, TimeSpan.Zero));

    [Fact(DisplayName = "BR-ED2-009: a chapter is titled from two constants, so nothing a broadcaster wrote reaches the artefact")]
    public void AChapterIsTitledFromTwoConstants()
    {
        string[] titles =
        [
            .. ChapterMetadataFile.Written(ThreeChapters, TimeSpan.FromSeconds(0.5))
                .Split('\n')
                .Where(line => line.StartsWith("title=", StringComparison.Ordinal)),
        ];

        Assert.Equal(["title=本編", "title=CM", "title=本編"], titles);
        Assert.Equal(2, titles.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact(DisplayName = "a moment between two milliseconds goes down as the nearer of them, so START and END never disagree by a tick nobody can see")]
    public void AMomentBetweenTwoMillisecondsGoesDownAsTheNearerOfThem()
        => Assert.Equal(
            """
            ;FFMETADATA1
            [CHAPTER]
            TIMEBASE=1/1000
            START=507
            END=1508
            title=本編

            """.ReplaceLineEndings("\n"),
            ChapterMetadataFile.Written(
                [new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1.0004), ChapterKind.Programme)],
                TimeSpan.FromSeconds(0.5072)));

    [Fact]
    public void AChaptersFileIsWrittenForChapters()
    {
        Assert.Throws<ArgumentException>(() => ChapterMetadataFile.Written([], TimeSpan.Zero));
        Assert.Throws<ArgumentNullException>(() => ChapterMetadataFile.Written(null!, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChapterMetadataFile.Written(ThreeChapters, TimeSpan.FromSeconds(-1)));
    }

    [Fact(DisplayName = "what lands on disk is what the writer says, and it opens with the header itself rather than with a mark of which way the bytes run")]
    public async Task WhatIsWrittenToTheFileIsWhatTheWriterSays()
    {
        using var room = new TempTree();
        string path = room.Under("breaks.chapters");

        await ChapterMetadataFile.WriteAsync(path, ThreeChapters, TimeSpan.FromSeconds(0.5), Cancel);

        byte[] landed = await File.ReadAllBytesAsync(path, Cancel);

        Assert.Equal(
            ChapterMetadataFile.Written(ThreeChapters, TimeSpan.FromSeconds(0.5)),
            await File.ReadAllTextAsync(path, Cancel));
        Assert.Equal((byte)';', landed[0]);
    }
}
