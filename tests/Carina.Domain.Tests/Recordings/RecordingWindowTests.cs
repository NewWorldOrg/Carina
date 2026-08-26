using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingWindowTests
{
    private static readonly DateTime Airs = new(2026, 8, 26, 20, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Head = TimeSpan.FromSeconds(15);

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
}
