using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SegmentAdoptionTests
{
    private static readonly DateTime Made = new(2026, 10, 1, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Adopted = new(2026, 10, 10, 3, 0, 0, DateTimeKind.Utc);

    private static readonly SegmentModelVersion InUse = SegmentModelVersion.Rehydrate(2, SegmentModelVersionState.Active, false, SegmentModel.First, Made, Adopted, null, null, null);

    private static readonly SegmentModelVersion ChosenByHand = SegmentModelVersion.Rehydrate(2, SegmentModelVersionState.Active, true, SegmentModel.First, Made, Adopted, null, Adopted, null);

    private static readonly SegmentModelVersion Before = SegmentModelVersion.Rehydrate(1, SegmentModelVersionState.Retired, false, SegmentModel.First, Made, Made, Adopted, null, null);

    private static readonly EvaluationEvidence Enough = new(12, 148, Made);

    [Theory(DisplayName = "BR-CD-008: a candidate is put in use when it finds more of the boundaries the catalog is sure of within two seconds, whatever it finds of the adverts' seconds")]
    [InlineData(0.93, 0.5, AdoptionVerdict.Adopted)]
    [InlineData(0.86, 0.9, AdoptionVerdict.Adopted)]
    [InlineData(0.80, 1.0, AdoptionVerdict.NoBetterAtBoundaries)]
    public void ACandidateMustFindMoreBoundaries(double recall, double seconds, AdoptionVerdict verdict)
    {
        Assert.Equal(verdict, SegmentAdoption.Judge(InUse, Evaluated(0.85, 0.9, Enough), Evaluated(recall, seconds, Enough)));
    }

    [Theory(DisplayName = "BR-CD-008: finding as many boundaries, the candidate is put in use only finding more of the adverts' seconds")]
    [InlineData(0.95, AdoptionVerdict.Adopted)]
    [InlineData(0.90, AdoptionVerdict.NoBetterAtBoundaries)]
    [InlineData(0.80, AdoptionVerdict.NoBetterAtBoundaries)]
    public void ATieOnBoundariesGoesToMoreSecondsOfAdverts(double seconds, AdoptionVerdict verdict)
    {
        Assert.Equal(verdict, SegmentAdoption.Judge(InUse, Evaluated(0.85, 0.9, Enough), Evaluated(0.85, seconds, Enough)));
    }

    [Fact(DisplayName = "BR-CD-008: a figure not known is less than any")]
    public void AFigureNotKnownIsLessThanAny()
    {
        SegmentMetrics unknown = new(null, null, [new KindMetrics(SegmentKind.Break, null)], null, 0);
        SegmentMetrics some = new(0, 0, [new KindMetrics(SegmentKind.Break, 0)], null, 0);

        Assert.True(SegmentAdoption.Better(some, unknown));
        Assert.False(SegmentAdoption.Better(unknown, some));
        Assert.False(SegmentAdoption.Better(unknown, unknown));
    }

    [Theory(DisplayName = "BR-CD-008: a better candidate is not put in use on fewer than ten recordings or thirty boundaries")]
    [InlineData(9, 148)]
    [InlineData(12, 29)]
    public void TooLittleEvidenceKeepsTheVersionInUse(int recordings, int boundaries)
    {
        EvaluationEvidence little = new(recordings, boundaries, Made);

        Assert.Equal(AdoptionVerdict.TooLittleEvidence, SegmentAdoption.Judge(InUse, Evaluated(0.5, 0.5, little), Evaluated(0.99, 0.99, little)));
        Assert.Equal(AdoptionVerdict.TooLittleEvidence, SegmentAdoption.Judge(InUse, new SegmentEvaluation(null, [], Enough), new SegmentEvaluation(null, [], Enough)));
    }

    [Fact(DisplayName = "a version chosen by hand is never replaced, however much better the candidate is")]
    public void AVersionChosenByHandStays()
    {
        Assert.Equal(AdoptionVerdict.ChosenByHand, SegmentAdoption.Judge(ChosenByHand, Evaluated(0.5, 0.5, Enough), Evaluated(0.99, 0.99, Enough)));
    }

    [Fact(DisplayName = "the version in use goes back when, within seven days, viewers came back from more than one and a half times the share of skips and skipped twenty times")]
    public void TooManyReturnsGoBack()
    {
        ViewerAction[] actions = [.. Actions(2, Adopted.AddDays(1), 25, 5), .. Actions(1, Adopted.AddDays(-3), 40, 4), .. Actions(1, Adopted.AddDays(-9), 40, 40)];

        SegmentRollback? rollback = SegmentRollback.Judge(InUse, Before, actions, Adopted.AddDays(6));

        Assert.Equal(new SegmentRollback(0.2, 0.1, 25, 1), rollback);
    }

    [Theory(DisplayName = "the version in use stays with returns no more than one and a half times as many, fewer than twenty skips, or after seven days")]
    [InlineData(20, 3, 6)]
    [InlineData(19, 19, 6)]
    [InlineData(25, 25, 8)]
    public void TheVersionInUseStaysOtherwise(int skips, int returned, int daysOn)
    {
        ViewerAction[] actions = [.. Actions(2, Adopted.AddDays(1), skips, returned), .. Actions(1, Adopted.AddDays(-3), 40, 4)];

        Assert.Null(SegmentRollback.Judge(InUse, Before, actions, Adopted.AddDays(daysOn)));
    }

    [Theory(DisplayName = "one and a half times the share before is counted in whole numbers: exactly that stays, and more goes back")]
    [InlineData(9, false)]
    [InlineData(10, true)]
    public void OneAndAHalfTimesIsCountedExactly(int returned, bool goesBack)
    {
        ViewerAction[] actions = [.. Actions(2, Adopted.AddDays(1), 20, returned), .. Actions(1, Adopted.AddDays(-3), 20, 6)];

        SegmentRollback? rollback = SegmentRollback.Judge(InUse, Before, actions, Adopted.AddDays(2));

        Assert.Equal(goesBack ? new SegmentRollback(returned / 20.0, 6 / 20.0, 20, 1) : null, rollback);
    }

    [Fact(DisplayName = "a version chosen by hand does not go back, and nothing is compared with a version before it nobody skipped with")]
    public void AVersionChosenByHandOrWithoutAComparisonStays()
    {
        ViewerAction[] returning = [.. Actions(2, Adopted.AddDays(1), 30, 30)];

        Assert.Null(SegmentRollback.Judge(ChosenByHand, Before, [.. returning, .. Actions(1, Adopted.AddDays(-3), 40, 0)], Adopted.AddDays(2)));
        Assert.Null(SegmentRollback.Judge(InUse, Before, returning, Adopted.AddDays(2)));
        Assert.Throws<ArgumentException>(() => SegmentRollback.Judge(Before, InUse, returning, Adopted.AddDays(2)));
    }

    [Fact(DisplayName = "BR-CD-008: for seven days after a version was rolled back from, a candidate that would be put in use is held out, and other verdicts stand")]
    public void ACandidateIsHeldOutAfterARollback()
    {
        DateTime rolledBack = Adopted.AddDays(1);
        SegmentModelVersion left = SegmentModelVersion.Rehydrate(3, SegmentModelVersionState.Retired, false, SegmentModel.First, Made, Adopted, rolledBack, null, rolledBack, rollback: new SegmentRollback(0.2, 0.1, 25, 2));
        SegmentModelVersion[] kept = [left, InUse, Before];

        Assert.Equal(AdoptionVerdict.HeldAfterRollback, SegmentAdoption.AfterRollbacks(AdoptionVerdict.Adopted, kept, rolledBack));
        Assert.Equal(AdoptionVerdict.HeldAfterRollback, SegmentAdoption.AfterRollbacks(AdoptionVerdict.Adopted, kept, rolledBack.AddDays(7)));
        Assert.Equal(AdoptionVerdict.Adopted, SegmentAdoption.AfterRollbacks(AdoptionVerdict.Adopted, kept, rolledBack.AddDays(7).AddTicks(1)));
        Assert.Equal(AdoptionVerdict.Adopted, SegmentAdoption.AfterRollbacks(AdoptionVerdict.Adopted, [InUse, Before], rolledBack));
        Assert.Equal(AdoptionVerdict.NoBetterAtBoundaries, SegmentAdoption.AfterRollbacks(AdoptionVerdict.NoBetterAtBoundaries, kept, rolledBack));
    }

    [Fact(DisplayName = "BR-CD-008: counted skips and returns judge a rollback as the actions they were counted from do")]
    public void CountedReturnsJudgeAsTheActionsDo()
    {
        ViewerAction[] actions = [.. Actions(2, Adopted.AddDays(1), 25, 5), .. Actions(1, Adopted.AddDays(-3), 40, 4), .. Actions(1, Adopted.AddDays(-9), 40, 40)];
        DateTime now = Adopted.AddDays(6);
        RollbackSpans spans = SegmentRollback.Spans(InUse, now)!;

        SegmentRollback? counted = SegmentRollback.Judge(
            InUse,
            Before,
            SkipReturns.Of(actions, InUse.Number, spans.From, spans.Until),
            SkipReturns.Of(actions, Before.Number, spans.BeforeFrom, spans.BeforeUntil),
            now);

        Assert.Equal(SegmentRollback.Judge(InUse, Before, actions, now), counted);
        Assert.Equal(new SegmentRollback(0.2, 0.1, 25, 1), counted);
        Assert.Null(SegmentRollback.Judge(InUse, Before, new SkipReturns(25, 25), new SkipReturns(40, 4), Adopted.AddDays(8)));
        Assert.Null(SegmentRollback.Judge(ChosenByHand, Before, new SkipReturns(25, 25), new SkipReturns(40, 4), now));
    }

    [Fact(DisplayName = "BR-CD-008: the version in use is judged from when it was put in use up to now, against the version before it over the seven days before that")]
    public void TheSpansRunFromThePutInUseAndTheSevenDaysBefore()
    {
        RollbackSpans? spans = SegmentRollback.Spans(InUse, Adopted.AddDays(3));

        Assert.Equal(new RollbackSpans(Adopted, Adopted.AddDays(3), Adopted.AddDays(-7), Adopted.AddTicks(-1)), spans);
        Assert.NotNull(SegmentRollback.Spans(InUse, Adopted.AddDays(7)));
        Assert.Null(SegmentRollback.Spans(InUse, Adopted.AddDays(7).AddTicks(1)));
        Assert.Null(SegmentRollback.Spans(InUse, Adopted.AddTicks(-1)));
        Assert.Null(SegmentRollback.Spans(ChosenByHand, Adopted.AddDays(3)));
        Assert.Throws<ArgumentException>(() => SegmentRollback.Spans(Before, Adopted.AddDays(3)));
    }

    [Fact(DisplayName = "BR-CD-008: the version before the one in use is the one taken out of use as it was put in use")]
    public void TheVersionBeforeIsTheOneItTookThePlaceOf()
    {
        SegmentModelVersion older = SegmentModelVersion.Rehydrate(3, SegmentModelVersionState.Retired, false, SegmentModel.First, Made, Made, Made.AddDays(2), null, null);
        SegmentModelVersion candidate = SegmentModelVersion.Rehydrate(4, SegmentModelVersionState.Candidate, false, SegmentModel.First, Adopted.AddDays(1), null, null, null, null);

        Assert.Equal(Before.Number, SegmentRollback.Previous([candidate, older, InUse, Before])?.Number);
        Assert.Null(SegmentRollback.Previous([candidate, HeldFirst()]));
        Assert.Throws<InvalidOperationException>(() => SegmentRollback.Previous([candidate, older]));
    }

    [Fact(DisplayName = "BR-CD-008: a version put back in use by a rollback is never rolled back to the version it was put back from")]
    public void AVersionPutBackIsNotRolledBackToTheOneItLeft()
    {
        SegmentModelVersion rolledBackFrom = SegmentModelVersion.Rehydrate(3, SegmentModelVersionState.Retired, false, SegmentModel.First, Made, Made, Adopted, null, Adopted, rollback: new SegmentRollback(0.2, 0.1, 25, 2));

        Assert.Null(SegmentRollback.Previous([rolledBackFrom, InUse]));
    }

    private static SegmentEvaluation Evaluated(double recall, double seconds, EvaluationEvidence evidence)
        => new(new SegmentMetrics(recall, recall - 0.1, [new KindMetrics(SegmentKind.Break, seconds), new KindMetrics(SegmentKind.Opening, null)], null, 0), [], evidence);

    private static SegmentModelVersion HeldFirst()
        => SegmentModelVersion.Rehydrate(SegmentModelVersion.FirstNumber, SegmentModelVersionState.Active, false, SegmentModel.First, Made, Made, null, null, null);

    private static IEnumerable<ViewerAction> Actions(int version, DateTime from, int skips, int returned)
        => Enumerable.Range(0, skips).Select(at => Action(ViewerActionKind.Skipped, version, from.AddMinutes(at)))
            .Concat(Enumerable.Range(0, returned).Select(at => Action(ViewerActionKind.Returned, version, from.AddMinutes(at).AddSeconds(30))));

    private static ViewerAction Action(ViewerActionKind action, int version, DateTime at)
        => new(at, action, SegmentKind.Break, TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(690), version);
}
