using Carina.Domain.Encodings;

namespace Carina.Domain.Tests.Encodings;

public sealed class ChapterGridTests
{
    private static readonly TimeSpan HalfAnHour = TimeSpan.FromMinutes(30);

    [Fact]
    public void ARecordingNothingWasObservedInIsMarkedNowhere()
    {
        ChapterDetection read = ChapterGrid.Mark(new ChapterEvidence(), HalfAnHour, new ChapterSettings());

        Assert.Equal(ChapterVerdict.NothingFound, read.Verdict);
        Assert.Empty(read.Segments);
        Assert.Equal(0d, read.BreakShare);
    }

    [Fact]
    public void OneBoundaryOnItsOwnMarksNothingHoweverStrongItIs()
    {
        ChapterDetection read = ChapterGrid.Mark(Observed(At(600)), HalfAnHour, new ChapterSettings());

        Assert.Equal(ChapterVerdict.NothingFound, read.Verdict);
        Assert.Empty(read.Segments);
    }

    [Fact]
    public void QuietOnItsOwnIsNotABoundaryHoweverMuchOfItThereIs()
    {
        ChapterEvidence heard = new()
        {
            Silences = [.. Enumerable.Range(1, 20).Select(step => Quiet(At(60 * step)))],
        };

        Assert.Equal(ChapterVerdict.NothingFound, ChapterGrid.Mark(heard, HalfAnHour, new ChapterSettings()).Verdict);
    }

    [Fact]
    public void TwoQuietStretchesAGridApartAreNoPairWhenOnlyOneOfThemIsCorroborated()
    {
        ChapterEvidence heard = new()
        {
            Silences = [Quiet(At(300)), Quiet(At(360))],
            Scenes = [new ChapterScene(At(300), 0.9)],
        };

        Assert.Equal(ChapterVerdict.NothingFound, ChapterGrid.Mark(heard, HalfAnHour, new ChapterSettings()).Verdict);
    }

    [Fact]
    public void APodOfAdvertisementsIsMarkedAndTheProgrammeAroundItIsLeftWithNoGap()
    {
        ChapterDetection read = ChapterGrid.Mark(Observed(At(300), At(360)), HalfAnHour, new ChapterSettings());

        Assert.Equal(ChapterVerdict.Marked, read.Verdict);
        Assert.Equal(3, read.Segments.Count);
        Assert.Equal(new ChapterSegment(TimeSpan.Zero, At(300), ChapterKind.Programme), read.Segments[0]);
        Assert.Equal(new ChapterSegment(At(300), At(360), ChapterKind.Break), read.Segments[1]);
        Assert.Equal(new ChapterSegment(At(360), HalfAnHour, ChapterKind.Programme), read.Segments[2]);
        Assert.Equal(1, read.Breaks);
        Assert.Equal(60d / 1800d, read.BreakShare, 6);
    }

    [Fact]
    public void EveryChapterStartsWhereTheOneBeforeItEndedAndTheLastOneEndsAtTheEnd()
    {
        ChapterDetection read = ChapterGrid.Mark(
            Observed(At(300), At(360), At(900), At(1020)),
            HalfAnHour,
            new ChapterSettings());

        Assert.Equal(ChapterVerdict.Marked, read.Verdict);
        Assert.Equal(TimeSpan.Zero, read.Segments[0].Starts);
        Assert.Equal(HalfAnHour, read.Segments[^1].Ends);
        Assert.All(
            read.Segments.Skip(1).Zip(read.Segments),
            pair => Assert.Equal(pair.Second.Ends, pair.First.Starts));
        Assert.Equal(2, read.Breaks);
        Assert.Equal(180d / 1800d, read.BreakShare, 6);
    }

    [Fact]
    public void APairOffTheGridByMoreThanTheToleranceIsNoPair()
    {
        Assert.Equal(
            ChapterVerdict.NothingFound,
            ChapterGrid.Mark(Observed(At(300), At(362.5)), HalfAnHour, new ChapterSettings()).Verdict);
    }

    [Fact]
    public void APairJustInsideTheToleranceIsAPairAndOneJustOutsideItIsNot()
    {
        var settings = new ChapterSettings();

        Assert.Equal(
            ChapterVerdict.Marked,
            ChapterGrid.Mark(Observed(At(300), At(360.9)), HalfAnHour, settings).Verdict);
        Assert.Equal(
            ChapterVerdict.NothingFound,
            ChapterGrid.Mark(Observed(At(300), At(361.1)), HalfAnHour, settings).Verdict);
    }

    [Fact]
    public void APodOffTheGridIsPulledOntoIt()
    {
        ChapterDetection read = ChapterGrid.Mark(Observed(At(300), At(360.7)), HalfAnHour, new ChapterSettings());

        ChapterSegment gap = read.Segments.Single(segment => segment.Kind is ChapterKind.Break);

        Assert.Equal(At(300), gap.Starts);
        Assert.Equal(At(360), gap.Ends);
        Assert.Equal(TimeSpan.FromSeconds(60), gap.Length);
    }

    [Fact]
    public void TwoPodsThatOverlapAreOnePod()
    {
        ChapterDetection read = ChapterGrid.Mark(
            Observed(At(300), At(330), At(360), At(420)),
            HalfAnHour,
            new ChapterSettings());

        ChapterSegment gap = read.Segments.Single(segment => segment.Kind is ChapterKind.Break);

        Assert.Equal(At(300), gap.Starts);
        Assert.Equal(At(420), gap.Ends);
    }

    [Fact]
    public void ASliverOfProgrammeBetweenTwoPodsIsNotWorthAMarkOfItsOwn()
    {
        ChapterDetection read = ChapterGrid.Mark(
            Observed(At(300), At(360), At(361.5), At(421.5)),
            HalfAnHour,
            new ChapterSettings());

        ChapterSegment gap = read.Segments.Single(segment => segment.Kind is ChapterKind.Break);

        Assert.Equal(At(300), gap.Starts);
        Assert.Equal(At(421.5), gap.Ends);
    }

    [Fact]
    public void APodThatStartsAlmostAtTheBeginningStartsAtTheBeginning()
    {
        ChapterDetection read = ChapterGrid.Mark(Observed(At(1.5), At(61.5)), HalfAnHour, new ChapterSettings());

        Assert.Equal(new ChapterSegment(TimeSpan.Zero, At(61.5), ChapterKind.Break), read.Segments[0]);
        Assert.Equal(2, read.Segments.Count);
    }

    [Fact]
    public void AReadingThatTookMoreOfTheLengthForBreaksThanIsAllowedIsThrownAwayWhole()
    {
        ChapterDetection read = ChapterGrid.Mark(Pods(9, every: 190, lasting: 105), HalfAnHour, new ChapterSettings());

        Assert.Equal(ChapterVerdict.Discarded, read.Verdict);
        Assert.Empty(read.Segments);
        Assert.Equal(945d / 1800d, read.BreakShare, 6);
    }

    [Fact]
    public void HowMuchOfTheLengthMayBeBreakIsWhatThisMachineWasTold()
    {
        ChapterEvidence heard = Observed(At(300), At(360));

        Assert.Equal(
            ChapterVerdict.Marked,
            ChapterGrid.Mark(heard, HalfAnHour, new ChapterSettings { MostBreakShare = 0.04 }).Verdict);
        Assert.Equal(
            ChapterVerdict.Discarded,
            ChapterGrid.Mark(heard, HalfAnHour, new ChapterSettings { MostBreakShare = 0.02 }).Verdict);
    }

    [Fact]
    public void AReadingWithMoreMarksInItThanAreAllowedIsThrownAwayWhole()
    {
        ChapterDetection read = ChapterGrid.Mark(
            Pods(21, every: 200, lasting: 15),
            TimeSpan.FromHours(2),
            new ChapterSettings());

        Assert.Equal(ChapterVerdict.Discarded, read.Verdict);
        Assert.Empty(read.Segments);
    }

    [Fact]
    public void HowManyMarksAreAllowedIsTheNumberThisMachineWasTold()
    {
        ChapterEvidence heard = Pods(3, every: 200, lasting: 15);

        Assert.Equal(
            ChapterVerdict.Marked,
            ChapterGrid.Mark(heard, HalfAnHour, new ChapterSettings { MostChapters = 6 }).Verdict);
        Assert.Equal(
            ChapterVerdict.Discarded,
            ChapterGrid.Mark(heard, HalfAnHour, new ChapterSettings { MostChapters = 5 }).Verdict);
    }

    [Fact]
    public void TheGridAPairIsLaidOnIsTheOneThisMachineWasTold()
    {
        ChapterEvidence heard = Observed(At(300), At(320));

        Assert.Equal(ChapterVerdict.NothingFound, ChapterGrid.Mark(heard, HalfAnHour, new ChapterSettings()).Verdict);
        Assert.Equal(
            ChapterVerdict.Marked,
            ChapterGrid.Mark(heard, HalfAnHour, new ChapterSettings { Grid = TimeSpan.FromSeconds(20) }).Verdict);
    }

    [Fact]
    public void HowFarOffTheGridAPairMaySitIsWhatThisMachineWasTold()
    {
        ChapterEvidence heard = Observed(At(300), At(362));

        Assert.Equal(ChapterVerdict.NothingFound, ChapterGrid.Mark(heard, HalfAnHour, new ChapterSettings()).Verdict);
        Assert.Equal(
            ChapterVerdict.Marked,
            ChapterGrid.Mark(heard, HalfAnHour, new ChapterSettings { GridTolerance = TimeSpan.FromSeconds(3) }).Verdict);
    }

    [Fact]
    public void HowMuchThePictureHasToChangeToCorroborateAQuietStretchIsWhatThisMachineWasTold()
    {
        ChapterEvidence heard = new()
        {
            Silences = [Quiet(At(300)), Quiet(At(360))],
            Scenes = [new ChapterScene(At(300), 0.20), new ChapterScene(At(360), 0.20)],
        };

        Assert.Equal(ChapterVerdict.NothingFound, ChapterGrid.Mark(heard, HalfAnHour, new ChapterSettings()).Verdict);
        Assert.Equal(
            ChapterVerdict.Marked,
            ChapterGrid.Mark(heard, HalfAnHour, new ChapterSettings { Scene = 0.10 }).Verdict);
    }

    [Fact]
    public void APictureThatWentBlackOverTheQuietCorroboratesItAsAChangeWould()
    {
        ChapterEvidence heard = new()
        {
            Silences = [Quiet(At(300)), Quiet(At(360))],
            Blacks = [new ChapterSpan(At(299.8), At(300.2)), new ChapterSpan(At(359.8), At(360.2))],
        };

        Assert.Equal(ChapterVerdict.Marked, ChapterGrid.Mark(heard, HalfAnHour, new ChapterSettings()).Verdict);
    }

    [Fact]
    public void AChangeTooFarFromTheQuietCorroboratesNothing()
    {
        ChapterEvidence heard = new()
        {
            Silences = [Quiet(At(300)), Quiet(At(360))],
            Scenes = [new ChapterScene(At(297), 0.9), new ChapterScene(At(357), 0.9)],
        };

        Assert.Equal(ChapterVerdict.NothingFound, ChapterGrid.Mark(heard, HalfAnHour, new ChapterSettings()).Verdict);
    }

    [Fact]
    public void APairFurtherApartThanAPodEverIsMarksNothing()
    {
        ChapterDetection read = ChapterGrid.Mark(
            Observed(At(300), At(600)),
            TimeSpan.FromHours(1),
            new ChapterSettings());

        Assert.Equal(ChapterVerdict.NothingFound, read.Verdict);
    }

    [Fact]
    public void WhatWasObservedPastTheEndOfTheRecordingIsNoBoundary()
    {
        ChapterDetection read = ChapterGrid.Mark(
            Observed(At(1740), At(1800), At(1860)),
            HalfAnHour,
            new ChapterSettings());

        ChapterSegment gap = read.Segments.Single(segment => segment.Kind is ChapterKind.Break);

        Assert.Equal(At(1740), gap.Starts);
        Assert.Equal(HalfAnHour, gap.Ends);
    }

    [Fact]
    public void ARecordingOfNoLengthIsNotSomethingToMark()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ChapterGrid.Mark(new ChapterEvidence(), TimeSpan.Zero, new ChapterSettings()));
    }

    [Fact]
    public void NothingIsReadWithoutSomethingObservedAndSomethingToReadItBy()
    {
        Assert.Throws<ArgumentNullException>(() => ChapterGrid.Mark(null!, HalfAnHour, new ChapterSettings()));
        Assert.Throws<ArgumentNullException>(() => ChapterGrid.Mark(new ChapterEvidence(), HalfAnHour, null!));
    }

    [Fact]
    public void AGridOfNoLengthIsNoGridToLayAPodOn()
    {
        Refused(new ChapterSettings { Grid = TimeSpan.Zero });
        Refused(new ChapterSettings { Grid = TimeSpan.FromSeconds(-15) });
    }

    [Fact]
    public void AToleranceHalfTheGridOrWiderIsRefusedWhereTheReadingIsMadeAndNotOnlyWhereItIsWritten()
    {
        Refused(new ChapterSettings { GridTolerance = TimeSpan.FromSeconds(7.5) });
        Refused(new ChapterSettings { GridTolerance = TimeSpan.FromSeconds(8) });
        Refused(new ChapterSettings { GridTolerance = TimeSpan.FromSeconds(-1) });
    }

    [Fact]
    public void AThresholdForThePictureThatIsNoShareOfTheWholeIsRefused()
    {
        Refused(new ChapterSettings { Scene = 0 });
        Refused(new ChapterSettings { Scene = 1.001 });
        Refused(new ChapterSettings { Scene = double.NaN });
    }

    [Fact]
    public void ASafetyValveSetToNoShareOfTheWholeIsRefusedRatherThanLeftOpen()
    {
        Refused(new ChapterSettings { MostBreakShare = 0 });
        Refused(new ChapterSettings { MostBreakShare = 1.001 });
        Refused(new ChapterSettings { MostBreakShare = double.NaN });
    }

    [Fact]
    public void AReadingIsAllowedAtLeastOneMark()
    {
        Refused(new ChapterSettings { MostChapters = 0 });
        Refused(new ChapterSettings { MostChapters = -1 });
    }

    [Fact(DisplayName = "a change scoring exactly what was asked for corroborates, so the filter that feeds this reads gte and not gt")]
    public void AChangeScoringExactlyWhatWasAskedForCorroborates()
    {
        ChapterEvidence heard = new()
        {
            Silences = [Quiet(At(300)), Quiet(At(360))],
            Scenes = [new ChapterScene(At(300), 0.30), new ChapterScene(At(360), 0.30)],
        };

        Assert.Equal(
            ChapterVerdict.Marked,
            ChapterGrid.Mark(heard, HalfAnHour, new ChapterSettings { Scene = 0.30 }).Verdict);
    }

    private static void Refused(ChapterSettings settings)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => ChapterGrid.Mark(Observed(At(300), At(360)), HalfAnHour, settings));

    private static TimeSpan At(double second) => TimeSpan.FromSeconds(second);

    private static ChapterSpan Quiet(TimeSpan around)
        => new(around - TimeSpan.FromSeconds(0.25), around + TimeSpan.FromSeconds(0.25));

    private static ChapterEvidence Observed(params TimeSpan[] boundaries)
        => new()
        {
            Silences = [.. boundaries.Select(Quiet)],
            Scenes = [.. boundaries.Select(at => new ChapterScene(at, 0.9))],
        };

    private static ChapterEvidence Pods(int many, double every, double lasting)
        => Observed(
            [.. Enumerable
                .Range(0, many)
                .SelectMany(step => new[] { At(100 + (every * step)), At(100 + (every * step) + lasting) })]);
}
