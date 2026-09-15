using Carina.Domain.Encodings;

namespace Carina.Domain.Tests.Encodings;

public sealed class ChapterDetectionLearningTests
{
    private static readonly TimeSpan Whole = TimeSpan.FromMinutes(10);

    [Fact(DisplayName = "BR-ED2-007: a reading carries nothing learned until a watermark is handed to it")]
    public void AReadingCarriesNothingLearnedUntilAWatermarkIsHandedToIt()
    {
        Assert.Null(ChapterDetection.NotAsked.Learned);
        Assert.Null(ChapterDetection.NothingFound().Learned);
        Assert.Null(ChapterDetection.Unreadable("could not read").Learned);
        Assert.Null(ChapterDetection.Discarded(0.7, "too much").Learned);
        Assert.Null(Marked().Learned);
    }

    [Fact(DisplayName = "BR-ED2-007: what was learned rides beside the reading and changes nothing that was read, before or after a note")]
    public void WhatWasLearnedRidesBesideTheReading()
    {
        WatermarkMask mask = WatermarkPictures.Learned();

        ChapterDetection noted = Marked().Learning(mask).Noting("part of the picture was looked at");
        ChapterDetection learnedAfter = Marked().Noting("part of the picture was looked at").Learning(mask);

        Assert.Same(mask, noted.Learned);
        Assert.Same(mask, learnedAfter.Learned);
        Assert.Equal(ChapterVerdict.Marked, noted.Verdict);
        Assert.Equal(Marked().Segments, noted.Segments);
        Assert.Equal(0.1, noted.BreakShare);
        Assert.Equal("part of the picture was looked at", learnedAfter.Note);
        Assert.Equal(noted.Note, learnedAfter.Note);
    }

    [Fact(DisplayName = "BR-ED2-007: a reading that could not be made still keeps what the look learned")]
    public void AReadingThatCouldNotBeMadeStillKeepsWhatWasLearned()
    {
        WatermarkMask mask = WatermarkPictures.Learned();

        ChapterDetection read = ChapterDetection.Unreadable("could not read").Learning(mask);

        Assert.Equal(ChapterVerdict.Unreadable, read.Verdict);
        Assert.Same(mask, read.Learned);
    }

    [Fact(DisplayName = "BR-ED2-007: nobody having looked, nothing can have been learned")]
    public void NobodyHavingLookedNothingCanHaveBeenLearned()
    {
        Assert.Throws<InvalidOperationException>(() => ChapterDetection.NotAsked.Learning(WatermarkPictures.Learned()));
    }

    private static ChapterDetection Marked()
        => ChapterDetection.Marked(
            [
                new ChapterSegment(TimeSpan.Zero, TimeSpan.FromMinutes(4), ChapterKind.Programme),
                new ChapterSegment(TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5), ChapterKind.Break),
                new ChapterSegment(TimeSpan.FromMinutes(5), Whole, ChapterKind.Programme),
            ],
            Whole,
            0.1);
}
