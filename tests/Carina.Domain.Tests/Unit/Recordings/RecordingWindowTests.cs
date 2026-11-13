using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Unit.Recordings;

[Trait("Category", "Unit")]
public sealed class RecordingWindowTests
{
    private static readonly DateTime Airs = new(2026, 8, 26, 20, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Head = TimeSpan.FromSeconds(15);

    private static readonly DateTime ReservedAirs = new(2026, 8, 24, 20, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Ends = ReservedAirs.AddMinutes(200);

    private static readonly Margin Ahead = Margin.OfSeconds(120);

    private static readonly Margin Behind = Margin.OfSeconds(180);

    private static readonly TimeSpan Lead = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan OneRoundOfTheTick = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan NoTunerForFiftyMinutes = TimeSpan.FromMinutes(50);

    [Fact]
    public void TheWindowOpensAfterTheHeadTuningIsAllowedToSpend()
    {
        RecordingWindow window = RecordingWindow.Promised(Airs, Airs.AddHours(1), Head);

        Assert.Equal(new DateTime(2026, 8, 26, 20, 0, 15, DateTimeKind.Utc), window.Start);
        Assert.Equal(new DateTime(2026, 8, 26, 21, 0, 0, DateTimeKind.Utc), window.End);
        Assert.Equal(TimeSpan.FromSeconds(15), window.Lead);
        Assert.Equal(TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(45), window.Length);
    }

    [Fact]
    public void TheEndOfThePromiseIsTheEndOfTheWindow()
    {
        RecordingWindow window = RecordingWindow.Promised(Airs, Airs.AddMinutes(30), Head);

        Assert.Equal(new DateTime(2026, 8, 26, 20, 30, 0, DateTimeKind.Utc), window.End);
    }

    [Fact]
    public void AHeadOfNothingLeavesThePromiseAsItWas()
    {
        RecordingWindow window = RecordingWindow.Promised(Airs, Airs.AddHours(1), TimeSpan.Zero);

        Assert.Equal(Airs, window.Start);
        Assert.Equal(TimeSpan.Zero, window.Lead);
    }

    [Fact]
    public void TheHeadNeverTakesMoreThanHalfOfWhatWasPromised()
    {
        DateTime twiceTheHead = Airs.AddSeconds(30);

        Assert.Equal(Head, RecordingWindow.Promised(Airs, twiceTheHead, Head).Lead);
        Assert.Equal(Head, RecordingWindow.Promised(Airs, twiceTheHead.AddTicks(1), Head).Lead);
        Assert.Equal(
            TimeSpan.FromSeconds(15) - TimeSpan.FromTicks(1),
            RecordingWindow.Promised(Airs, twiceTheHead.AddTicks(-1), Head).Lead);
    }

    [Fact]
    public void APromiseShorterThanTheHeadIsHalvedRatherThanEmptied()
    {
        RecordingWindow window = RecordingWindow.Promised(Airs, Airs.AddSeconds(4), Head);

        Assert.Equal(TimeSpan.FromSeconds(2), window.Lead);
        Assert.Equal(new DateTime(2026, 8, 26, 20, 0, 2, DateTimeKind.Utc), window.Start);
        Assert.True(window.Length > TimeSpan.Zero);
    }

    [Fact]
    public void APromiseThatDoesNotRunForwardsIsNoPromiseAtAll()
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(
            () => RecordingWindow.Promised(Airs, Airs, Head));

        Assert.Equal("effectiveEndAt", refused.ParamName);
        Assert.Equal(
            "effectiveEndAt",
            Assert.Throws<ArgumentException>(
                () => RecordingWindow.Promised(Airs, Airs.AddTicks(-1), Head)).ParamName);
        Assert.Equal(Airs.AddTicks(1), RecordingWindow.Promised(Airs, Airs.AddTicks(1), Head).End);
    }

    [Fact]
    public void AHeadThatRunsBackwardsIsRefused()
    {
        ArgumentOutOfRangeException refused = Assert.Throws<ArgumentOutOfRangeException>(
            () => RecordingWindow.Promised(Airs, Airs.AddHours(1), TimeSpan.FromTicks(-1)));

        Assert.Equal("tuningLead", refused.ParamName);
        Assert.Equal(Airs, RecordingWindow.Promised(Airs, Airs.AddHours(1), TimeSpan.Zero).Start);
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void AWindowIsPromisedInUtcOrNotAtAll(DateTimeKind kind)
    {
        Assert.Equal(
            "effectiveStartAt",
            Assert.Throws<ArgumentException>(
                () => RecordingWindow.Promised(DateTime.SpecifyKind(Airs, kind), Airs.AddHours(1), Head)).ParamName);

        Assert.Equal(
            "effectiveEndAt",
            Assert.Throws<ArgumentException>(
                () => RecordingWindow.Promised(Airs, DateTime.SpecifyKind(Airs.AddHours(1), kind), Head)).ParamName);
    }

    [Fact]
    public void ARecordingThatSpentTheHeadTuningIsCompleteAgainstTheWindowItWasGiven()
    {
        RecordingWindow window = RecordingWindow.Promised(Airs, Airs.AddMinutes(30), Head);
        RecordingVerdict verdict = Judged(window.Start, window.End);

        Assert.Equal(RecordingOutcome.Complete, verdict.Outcome);
        Assert.Empty(verdict.Faults);
        Assert.Equal(1.0, verdict.Coverage, 6);
    }

    [Fact]
    public void TheSameRecordingMeasuredFromTheMomentItWasAskedForFallsShort()
    {
        RecordingVerdict verdict = Judged(Airs, Airs.AddMinutes(30));

        Assert.Equal(RecordingOutcome.Truncated, verdict.Outcome);
        Assert.Contains(RecordingFault.ShortOfTheWindow, verdict.Faults);
        Assert.True(verdict.Coverage < CompletionTolerance.Default.CompleteCoverage);
    }

    private static RecordingVerdict Judged(DateTime windowStart, DateTime windowEnd)
    {
        TimeSpan written = TimeSpan.FromSeconds(1785);

        return CompletionEvaluator.Judge(
            new RecordingEvidence(
                Weighing(written),
                written,
                windowStart,
                windowEnd,
                Airs.AddMinutes(30)),
            ExpectedBitrate.Terrestrial,
            CompletionTolerance.Default);
    }

    private static long Weighing(TimeSpan written)
        => (long)((ExpectedBitrate.Terrestrial.LeastBitsPerSecond
            + ExpectedBitrate.Terrestrial.MostBitsPerSecond)
            * written.TotalSeconds
            / 16.0);

    [Fact]
    public void AskedForAheadOfTimeAndStartedOnTheDotItCoversAllOfItsWindow()
    {
        RecordingWindow window = Promised(ReservedAirs.AddHours(-1));

        Assert.Equal(ReservedAirs.AddSeconds(-90), window.Start);
        Assert.Equal(Ends.AddSeconds(180), window.End);

        RecordingVerdict verdict = Judge(window, window.Length);

        Assert.Equal(1.0, verdict.Coverage);
        Assert.Equal(RecordingOutcome.Complete, verdict.Outcome);
    }

    [Fact]
    public void AskedForWhileTheBroadcastIsAlreadyRunningItIsPromisedFromThatMomentAndCoversAlmostAllOfIt()
    {
        DateTime asking = ReservedAirs.AddMinutes(50);
        RecordingWindow window = Promised(asking);

        Assert.Equal(asking + Lead, window.Start);
        Assert.Equal(Ends.AddSeconds(180), window.End);

        RecordingVerdict verdict = Judge(window, window.Length - OneRoundOfTheTick);

        Assert.InRange(verdict.Coverage, 0.995, 1.0);
        Assert.Equal(RecordingOutcome.Complete, verdict.Outcome);
    }

    [Fact]
    public void AskedForTooLateForItsOwnMarginItIsPromisedFromTheAskingAndCoversAlmostAllOfIt()
    {
        DateTime asking = ReservedAirs.AddSeconds(-60);
        RecordingWindow window = Promised(asking);

        Assert.Equal(asking + Lead, window.Start);
        Assert.Equal(Ends.AddSeconds(180), window.End);

        RecordingVerdict verdict = Judge(window, window.Length - OneRoundOfTheTick);

        Assert.InRange(verdict.Coverage, 0.995, 1.0);
        Assert.Equal(RecordingOutcome.Complete, verdict.Outcome);
    }

    [Fact]
    public void AskedForAheadOfTimeButStartedFiftyMinutesLateItIsShortOfItsWindowAndSaysSo()
    {
        RecordingWindow window = Promised(ReservedAirs.AddHours(-1));

        RecordingVerdict verdict = Judge(window, window.Length - NoTunerForFiftyMinutes);

        Assert.InRange(verdict.Coverage, 0.74, 0.76);
        Assert.Contains(RecordingFault.ShortOfTheWindow, verdict.Faults);
        Assert.Equal(RecordingOutcome.Truncated, verdict.Outcome);
    }

    [Fact]
    public void TheLatenessIsStillSeenWhenTheBroadcastWasAlreadyRunningWhenItWasAskedFor()
    {
        DateTime asking = ReservedAirs.AddMinutes(50);
        RecordingWindow window = Promised(asking);

        RecordingVerdict verdict = Judge(window, window.Length - NoTunerForFiftyMinutes);

        Assert.InRange(verdict.Coverage, 0.66, 0.68);
        Assert.Equal(RecordingOutcome.Truncated, verdict.Outcome);
    }

    private static RecordingWindow Promised(DateTime askedAt)
    {
        Reservation asked = Reservation.Plan(
            ReservationId.New(),
            new ProgrammeRef(new NetworkId(32736), new ServiceId(1024), new EventId(4001), ReservedAirs),
            null,
            Priority.Default,
            ReservedAirs,
            Ends,
            true,
            Ahead,
            Behind,
            new ProgrammeSnapshot(
                "A programme",
                "What it is about",
                string.Empty,
                [new ProgrammeGenre(7, 1)],
                ReservedAirs.AddHours(-6),
                AudioMode.Undetermined,
                ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone,
            askedAt);

        return RecordingWindow.Promised(asked.EffectiveStartAt, asked.EffectiveEndAt, Lead);
    }

    private static RecordingVerdict Judge(RecordingWindow window, TimeSpan written)
        => CompletionEvaluator.Judge(
            new RecordingEvidence(Weighing(written), written, window.Start, window.End, window.End),
            ExpectedBitrate.Terrestrial,
            CompletionTolerance.Default);
}
