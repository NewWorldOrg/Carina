using Carina.Domain.Base;
using Carina.Domain.Encodings;

namespace Carina.Domain.Tests.Encodings;

public sealed class ChapterDetectionTests
{
    private static readonly TimeSpan Whole = TimeSpan.FromMinutes(10);

    [Fact]
    public void NobodyHavingLookedIsAnAnswerWithNothingOnIt()
    {
        ChapterDetection read = ChapterDetection.NotAsked;

        Assert.Equal(ChapterVerdict.NotAsked, read.Verdict);
        Assert.Empty(read.Segments);
        Assert.Equal(0d, read.BreakShare);
        Assert.Equal(string.Empty, read.Note);
        Assert.False(read.Marks);
        Assert.Equal(0, read.Breaks);
    }

    [Fact]
    public void HavingLookedAndFoundNothingIsAnAnswerWithNothingOnIt()
    {
        ChapterDetection read = ChapterDetection.NothingFound();

        Assert.Equal(ChapterVerdict.NothingFound, read.Verdict);
        Assert.Empty(read.Segments);
        Assert.Equal(0d, read.BreakShare);
        Assert.Equal(string.Empty, read.Note);
        Assert.False(read.Marks);
    }

    [Fact]
    public void AReadingLaidEndToEndOverTheWholeIsMarked()
    {
        ChapterDetection read = ChapterDetection.Marked(Laid(), Whole, 0.1);

        Assert.Equal(ChapterVerdict.Marked, read.Verdict);
        Assert.True(read.Marks);
        Assert.Equal(3, read.Segments.Count);
        Assert.Equal(1, read.Breaks);
        Assert.Equal(0.1, read.BreakShare);
        Assert.Equal(string.Empty, read.Note);
    }

    [Fact]
    public void WhatAMarkedReadingHoldsIsItsOwnAndNotTheListItWasHanded()
    {
        List<ChapterSegment> handed = [.. Laid()];

        ChapterDetection read = ChapterDetection.Marked(handed, Whole, 0.1);
        handed.Clear();

        Assert.Equal(3, read.Segments.Count);
    }

    [Fact]
    public void AMarkedReadingIsHandedSomethingToMark()
    {
        Assert.Throws<ArgumentNullException>(() => ChapterDetection.Marked(null!, Whole, 0));
        Assert.Throws<ArgumentException>(() => ChapterDetection.Marked([], Whole, 0));
    }

    [Fact]
    public void AMarkedReadingStartsWhereTheArtefactStarts()
    {
        Assert.Throws<ArgumentException>(() => ChapterDetection.Marked(
            [Chapter(1, 4, ChapterKind.Break), Chapter(4, 10, ChapterKind.Programme)],
            Whole,
            0.3));
    }

    [Fact]
    public void AMarkedReadingLeavesNoGapBetweenOneChapterAndTheNext()
    {
        Assert.Throws<ArgumentException>(() => ChapterDetection.Marked(
            [Chapter(0, 3, ChapterKind.Programme), Chapter(4, 10, ChapterKind.Break)],
            Whole,
            0.6));
    }

    [Fact]
    public void AMarkedReadingTurnsFromProgrammeToBreakAndBackAtEveryMark()
    {
        Assert.Throws<ArgumentException>(() => ChapterDetection.Marked(
            [Chapter(0, 3, ChapterKind.Programme), Chapter(3, 4, ChapterKind.Programme), Chapter(4, 10, ChapterKind.Break)],
            Whole,
            0.6));
    }

    [Fact]
    public void AMarkedReadingReachesTheEndOfTheArtefactAndStopsThere()
    {
        Assert.Throws<ArgumentException>(() => ChapterDetection.Marked(
            [Chapter(0, 3, ChapterKind.Programme), Chapter(3, 4, ChapterKind.Break)],
            Whole,
            0.1));
        Assert.Throws<ArgumentException>(() => ChapterDetection.Marked(
            [Chapter(0, 3, ChapterKind.Programme), Chapter(3, 11, ChapterKind.Break)],
            Whole,
            0.8));
    }

    [Fact]
    public void AReadingOfNothingButProgrammeIsNotAMarkedOne()
    {
        Assert.Throws<ArgumentException>(
            () => ChapterDetection.Marked([Chapter(0, 10, ChapterKind.Programme)], Whole, 0));
    }

    [Theory]
    [InlineData(-0.001)]
    [InlineData(1.001)]
    [InlineData(double.NaN)]
    public void AShareOfTheLengthThatIsNoShareOfItIsRefused(double share)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ChapterDetection.Marked(Laid(), Whole, share));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChapterDetection.Discarded(share, "too much of it"));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(1d)]
    public void NoneOfTheLengthAndAllOfItAreBothSharesOfIt(double share)
    {
        Assert.Equal(share, ChapterDetection.Discarded(share, "too much of it").BreakShare);
    }

    [Fact]
    public void AReadingThrownAwayKeepsWhatItTookAndWhyItWentAndMarksNothing()
    {
        ChapterDetection read = ChapterDetection.Discarded(0.7, "the breaks came to too much of the length");

        Assert.Equal(ChapterVerdict.Discarded, read.Verdict);
        Assert.Empty(read.Segments);
        Assert.False(read.Marks);
        Assert.Equal(0.7, read.BreakShare);
        Assert.Equal("the breaks came to too much of the length", read.Note);
    }

    [Fact]
    public void ASourceThatCouldNotBeReadKeepsWhyAndTookNoneOfTheLength()
    {
        ChapterDetection read = ChapterDetection.Unreadable("the programme could not be started");

        Assert.Equal(ChapterVerdict.Unreadable, read.Verdict);
        Assert.Empty(read.Segments);
        Assert.False(read.Marks);
        Assert.Equal(0d, read.BreakShare);
        Assert.Equal("the programme could not be started", read.Note);
    }

    [Fact]
    public void ANoteKeepsNeitherThePathsOnThisMachineNorMoreThanItIsAllowed()
    {
        Assert.Equal("… could not be opened", ChapterDetection.Unreadable("/srv/recordings/a.ts could not be opened").Note);
        Assert.Equal(ProgrammeNote.Longest, ChapterDetection.Discarded(0.5, new string('x', ProgrammeNote.Longest + 10)).Note.Length);
    }

    private static IReadOnlyList<ChapterSegment> Laid() =>
    [
        Chapter(0, 3, ChapterKind.Programme),
        Chapter(3, 4, ChapterKind.Break),
        Chapter(4, 10, ChapterKind.Programme),
    ];

    private static ChapterSegment Chapter(double from, double to, ChapterKind kind)
        => new(TimeSpan.FromMinutes(from), TimeSpan.FromMinutes(to), kind);
}
