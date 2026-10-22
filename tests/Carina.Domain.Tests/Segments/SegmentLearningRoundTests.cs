using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SegmentLearningRoundTests
{
    [Theory(DisplayName = "BR-CD-004: a day of learning runs from three in the morning in Japan to three the next morning")]
    [InlineData("2026-10-18T18:00:00Z", "2026-10-19")]
    [InlineData("2026-10-18T17:59:59Z", "2026-10-18")]
    [InlineData("2026-10-19T14:59:59Z", "2026-10-19")]
    [InlineData("2026-10-19T15:00:00Z", "2026-10-19")]
    [InlineData("2026-10-19T17:59:59Z", "2026-10-19")]
    public void ADayRunsFromThreeInTheMorningInJapan(string at, string day)
    {
        DateTime moment = DateTime.Parse(at, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal);

        Assert.Equal(DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture), SegmentLearningRound.DayOf(moment));
        Assert.Equal(SegmentLearningRound.DayOf(moment), SegmentLearningRound.Taken(moment).Day);
    }

    [Theory(DisplayName = "BR-CD-004: a round learns while no round has kept a candidate, and otherwise only when something changed after the last one that did was taken")]
    [InlineData(null, null, true)]
    [InlineData(null, "2026-10-19T03:00:00Z", true)]
    [InlineData("2026-10-19T03:00:00Z", null, false)]
    [InlineData("2026-10-19T03:00:00Z", "2026-10-19T03:00:00Z", false)]
    [InlineData("2026-10-19T03:00:00Z", "2026-10-19T02:59:59Z", false)]
    [InlineData("2026-10-19T03:00:00Z", "2026-10-19T03:00:01Z", true)]
    public void ARoundLearnsOnlyFromSomethingNew(string? lastLearnedFrom, string? lastChanged, bool news)
    {
        Assert.Equal(news, SegmentLearningRound.HasNews(Moment(lastLearnedFrom), Moment(lastChanged)));
    }

    [Fact(DisplayName = "a round is taken in universal time, no earlier than the day it is for")]
    public void ARoundIsTakenNoEarlierThanItsDay()
    {
        DateTime at = new(2026, 10, 19, 18, 30, 0, DateTimeKind.Utc);

        Assert.Equal(new DateOnly(2026, 10, 18), SegmentLearningRound.Rehydrate(new DateOnly(2026, 10, 18), at, null).Day);
        Assert.Throws<ArgumentException>(() => SegmentLearningRound.Rehydrate(new DateOnly(2026, 10, 21), at, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => SegmentLearningRound.Rehydrate(new DateOnly(2026, 10, 18), at, 0));
        Assert.Throws<ArgumentException>(() => SegmentLearningRound.Taken(DateTime.SpecifyKind(at, DateTimeKind.Local)));
    }

    [Theory(DisplayName = "BR-CD-004: the next round may begin at three in the morning in Japan of the day it is while that day's round is not finished, and of the next day once it is")]
    [InlineData("2026-10-20T00:00:00Z", false, "2026-10-19T18:00:00Z")]
    [InlineData("2026-10-20T00:00:00Z", true, "2026-10-20T18:00:00Z")]
    [InlineData("2026-10-19T17:59:59Z", false, "2026-10-18T18:00:00Z")]
    [InlineData("2026-10-19T17:59:59Z", true, "2026-10-19T18:00:00Z")]
    [InlineData("2026-10-19T18:00:00Z", true, "2026-10-20T18:00:00Z")]
    public void TheNextRoundMayBeginAtThreeInTheMorningInJapan(string now, bool finishedToday, string notBefore)
    {
        DateTime next = SegmentLearningRound.NextNotBefore(Moment(now)!.Value, finishedToday);

        Assert.Equal(Moment(notBefore), next);
        Assert.Equal(DateTimeKind.Utc, next.Kind);
    }

    [Fact(DisplayName = "BR-CD-008: a round that learned a candidate is kept with what became of it, the verdict on the candidate and the figures of the version in use beside it")]
    public void ARoundThatLearnedIsKeptWithHowItWasJudged()
    {
        DateTime at = new(2026, 10, 19, 18, 30, 0, DateTimeKind.Utc);
        SegmentMetrics inUse = new(0.08, 0.05, [new KindMetrics(SegmentKind.Break, 0.4)], null, 0);

        SegmentLearningRound round = SegmentLearningRound.Ended(SegmentLearningRound.DayOf(at), at, SegmentTrainingOutcome.Kept, 5, AdoptionVerdict.TooLittleEvidence, inUse);

        Assert.Equal(
            (new DateOnly(2026, 10, 20), at, (int?)5, (SegmentTrainingOutcome?)SegmentTrainingOutcome.Kept, (AdoptionVerdict?)AdoptionVerdict.TooLittleEvidence, inUse),
            (round.Day, round.TakenAt, round.Candidate, round.Outcome, round.Verdict, round.InUseMetrics));
    }

    [Fact(DisplayName = "BR-CD-008: a round that learned a candidate is kept with the number of the version in use it measured, which a round kept before the number was kept may lack")]
    public void ARoundThatLearnedIsKeptWithTheNumberOfTheVersionItMeasured()
    {
        DateTime at = new(2026, 10, 19, 18, 30, 0, DateTimeKind.Utc);
        DateOnly day = SegmentLearningRound.DayOf(at);
        SegmentMetrics inUse = new(0.08, 0.05, [], null, 0);

        Assert.Equal(3, SegmentLearningRound.Ended(day, at, SegmentTrainingOutcome.Kept, 5, AdoptionVerdict.TooLittleEvidence, inUse, 3).InUseVersion);
        Assert.Equal(3, SegmentLearningRound.Rehydrate(day, at, 5, SegmentTrainingOutcome.Kept, AdoptionVerdict.TooLittleEvidence, inUse, 3).InUseVersion);
        Assert.Null(SegmentLearningRound.Ended(day, at, SegmentTrainingOutcome.Kept, 5, AdoptionVerdict.TooLittleEvidence, inUse).InUseVersion);
        Assert.Null(SegmentLearningRound.Ended(day, at, SegmentTrainingOutcome.NothingNew).InUseVersion);
    }

    [Fact(DisplayName = "BR-CD-008: the number of the version in use a round measured is a version number, and only a round that kept a candidate has one")]
    public void TheNumberOfTheVersionMeasuredIsAVersionNumberOfARoundThatKeptACandidate()
    {
        DateTime at = new(2026, 10, 19, 18, 30, 0, DateTimeKind.Utc);
        DateOnly day = SegmentLearningRound.DayOf(at);

        Assert.Throws<ArgumentOutOfRangeException>(() => SegmentLearningRound.Ended(day, at, SegmentTrainingOutcome.Kept, 5, AdoptionVerdict.TooLittleEvidence, null, 0));
        Assert.Throws<ArgumentException>(() => SegmentLearningRound.Ended(day, at, SegmentTrainingOutcome.NothingNew, null, null, null, 1));
        Assert.Throws<ArgumentException>(() => SegmentLearningRound.Rehydrate(day, at, null, null, null, null, 1));
    }

    [Theory(DisplayName = "BR-CD-004: a round that learned nothing, or failed, is kept with what became of it and nothing of a candidate")]
    [InlineData(SegmentTrainingOutcome.NothingNew)]
    [InlineData(SegmentTrainingOutcome.NothingToLearnFrom)]
    [InlineData(SegmentTrainingOutcome.Failed)]
    public void ARoundThatLearnedNothingKeepsNoCandidate(SegmentTrainingOutcome outcome)
    {
        DateTime at = new(2026, 10, 19, 18, 30, 0, DateTimeKind.Utc);

        SegmentLearningRound round = SegmentLearningRound.Ended(SegmentLearningRound.DayOf(at), at, outcome);

        Assert.Equal(((SegmentTrainingOutcome?)outcome, (int?)null, (AdoptionVerdict?)null, (SegmentMetrics?)null), (round.Outcome, round.Candidate, round.Verdict, round.InUseMetrics));
        Assert.Throws<ArgumentException>(() => SegmentLearningRound.Ended(round.Day, at, outcome, 5, AdoptionVerdict.TooLittleEvidence));
    }

    [Fact(DisplayName = "BR-CD-004: a round that stopped partway is never kept as finished, and one that learned a candidate keeps it with the verdict on it")]
    public void ARoundIsKeptOnlyAsItEnded()
    {
        DateTime at = new(2026, 10, 19, 18, 30, 0, DateTimeKind.Utc);
        DateOnly day = SegmentLearningRound.DayOf(at);
        SegmentMetrics inUse = new(0.08, 0.05, [], null, 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => SegmentLearningRound.Ended(day, at, SegmentTrainingOutcome.Stopped));
        Assert.Throws<ArgumentException>(() => SegmentLearningRound.Ended(day, at, SegmentTrainingOutcome.Kept));
        Assert.Throws<ArgumentException>(() => SegmentLearningRound.Ended(day, at, SegmentTrainingOutcome.Adopted, 5));
        Assert.Throws<ArgumentException>(() => SegmentLearningRound.Rehydrate(day, at, null, null, AdoptionVerdict.Adopted));
        Assert.Throws<ArgumentException>(() => SegmentLearningRound.Rehydrate(day, at, null, null, null, inUse));
        Assert.Null(SegmentLearningRound.Rehydrate(day, at, 5).Outcome);
    }

    private static DateTime? Moment(string? at)
        => at is null ? null : DateTime.Parse(at, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal);
}
