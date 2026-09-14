using Carina.Domain.Encodings;
using Carina.Infrastructure.Encodings;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class ChapterClockTests
{
    private static readonly TimeSpan Broadcast = TimeSpan.FromSeconds(62170.5);

    private static readonly TimeSpan HeadSkip = TimeSpan.FromSeconds(0.483878);

    private static readonly TimeSpan Artefact = TimeSpan.FromSeconds(7195.521667);

    [Fact(DisplayName = "a moment reported on the clock the broadcast was carrying has that clock's beginning taken off it")]
    public void AMomentOnTheBroadcastsOwnClockHasItsBeginningTakenOff()
        => Assert.Equal(
            TimeSpan.FromSeconds(300) - HeadSkip,
            ChapterClock.OnTheArtefact(Broadcast + TimeSpan.FromSeconds(300), Broadcast, HeadSkip, Artefact));

    [Fact(DisplayName = "a moment already counted from the beginning of the source is left where it is and only the skipped head comes off")]
    public void AMomentAlreadyCountedFromTheBeginningIsLeftWhereItIs()
        => Assert.Equal(
            TimeSpan.FromSeconds(300) - HeadSkip,
            ChapterClock.OnTheArtefact(TimeSpan.FromSeconds(300), Broadcast, HeadSkip, Artefact));

    [Fact(DisplayName = "the head the encode skips is what the artefact's own zero is, so a moment inside it is no moment of the artefact")]
    public void AMomentInsideTheSkippedHeadIsNoMomentOfTheArtefact()
    {
        Assert.Equal(TimeSpan.Zero, ChapterClock.OnTheArtefact(Broadcast + HeadSkip, Broadcast, HeadSkip, Artefact));
        Assert.Null(ChapterClock.OnTheArtefact(Broadcast + TimeSpan.FromSeconds(0.4), Broadcast, HeadSkip, Artefact));
    }

    [Fact(DisplayName = "a moment past the end of the artefact is thrown away rather than pulled back to the end")]
    public void AMomentPastTheEndIsThrownAwayRatherThanPulledBack()
    {
        Assert.Equal(Artefact, ChapterClock.OnTheArtefact(Broadcast + HeadSkip + Artefact, Broadcast, HeadSkip, Artefact));
        Assert.Null(ChapterClock.OnTheArtefact(
            Broadcast + HeadSkip + Artefact + TimeSpan.FromSeconds(0.001),
            Broadcast,
            HeadSkip,
            Artefact));
        Assert.Null(ChapterClock.OnTheArtefact(TimeSpan.FromSeconds(200000), Broadcast, HeadSkip, Artefact));
    }

    [Fact(DisplayName = "a source that begins at zero needs nothing taken off it")]
    public void ASourceThatBeginsAtZeroNeedsNothingTakenOffIt()
        => Assert.Equal(
            TimeSpan.FromSeconds(30),
            ChapterClock.OnTheArtefact(TimeSpan.FromSeconds(30), TimeSpan.Zero, TimeSpan.Zero, Artefact));

    [Fact(DisplayName = "a stretch is placed by both of its ends, and one end outside the artefact places neither")]
    public void AStretchIsPlacedByBothOfItsEnds()
    {
        ChapterSpan? placed = ChapterClock.OnTheArtefact(
            new ChapterSpan(Broadcast + TimeSpan.FromSeconds(300), Broadcast + TimeSpan.FromSeconds(300.5)),
            Broadcast,
            HeadSkip,
            Artefact);

        Assert.NotNull(placed);
        Assert.Equal(TimeSpan.FromSeconds(300) - HeadSkip, placed.Value.Starts);
        Assert.Equal(TimeSpan.FromSeconds(300.5) - HeadSkip, placed.Value.Ends);

        Assert.Null(ChapterClock.OnTheArtefact(
            new ChapterSpan(Broadcast + HeadSkip + Artefact - TimeSpan.FromSeconds(0.2), Broadcast + HeadSkip + Artefact + TimeSpan.FromSeconds(5)),
            Broadcast,
            HeadSkip,
            Artefact));
    }

    [Fact(DisplayName = "a stretch that ends where it began is no stretch")]
    public void AStretchThatEndsWhereItBeganIsNoStretch()
        => Assert.Null(ChapterClock.OnTheArtefact(
            new ChapterSpan(Broadcast + TimeSpan.FromSeconds(300), Broadcast + TimeSpan.FromSeconds(300)),
            Broadcast,
            HeadSkip,
            Artefact));

    [Theory(DisplayName = "a quarter of the moments falling outside is still a reading; more than a quarter is a clock nobody can name")]
    [InlineData(0, 0, false)]
    [InlineData(0, 100, false)]
    [InlineData(25, 100, false)]
    [InlineData(26, 100, true)]
    [InlineData(1, 4, false)]
    [InlineData(2, 4, true)]
    [InlineData(1, 1, true)]
    public void MoreThanAQuarterOutsideIsAClockNobodyCanName(int outOfReach, int reported, bool thrownAway)
        => Assert.Equal(thrownAway, ChapterClock.TooMuchOutOfReach(outOfReach, reported));

    [Fact(DisplayName = "what the reading is measured against is checked before anything is measured")]
    public void WhatTheReadingIsMeasuredAgainstIsCheckedFirst()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ChapterClock.OnTheArtefact(TimeSpan.Zero, TimeSpan.FromSeconds(-1), TimeSpan.Zero, Artefact));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ChapterClock.OnTheArtefact(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(-1), Artefact));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ChapterClock.OnTheArtefact(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChapterClock.TooMuchOutOfReach(-1, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChapterClock.TooMuchOutOfReach(11, 10));
    }
}
