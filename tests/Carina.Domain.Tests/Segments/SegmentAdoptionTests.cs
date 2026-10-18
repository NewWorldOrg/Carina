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

    [Theory(DisplayName = "a candidate is put in use when it finds more boundaries within two seconds and skips no more than a second of programme an hour more")]
    [InlineData(0.93, 3.0, AdoptionVerdict.Adopted)]
    [InlineData(0.93, 1.0, AdoptionVerdict.Adopted)]
    [InlineData(0.93, 3.5, AdoptionVerdict.SkipsMoreProgramme)]
    [InlineData(0.80, 1.0, AdoptionVerdict.NoBetterAtBoundaries)]
    [InlineData(0.85, 2.0, AdoptionVerdict.NoBetterAtBoundaries)]
    public void ACandidateMustFindMoreWithoutSkippingMoreProgramme(double recall, double skipped, AdoptionVerdict verdict)
    {
        Assert.Equal(verdict, SegmentAdoption.Judge(InUse, Evaluated(0.85, 1.2, 2.0, Enough), Evaluated(recall, 1.2, skipped, Enough)));
    }

    [Theory(DisplayName = "finding as many boundaries, the candidate is put in use only with fewer extra ones")]
    [InlineData(0.8, AdoptionVerdict.Adopted)]
    [InlineData(1.2, AdoptionVerdict.NoBetterAtBoundaries)]
    [InlineData(1.6, AdoptionVerdict.NoBetterAtBoundaries)]
    public void ATieOnBoundariesGoesToFewerExtras(double extra, AdoptionVerdict verdict)
    {
        Assert.Equal(verdict, SegmentAdoption.Judge(InUse, Evaluated(0.85, 1.2, 2.0, Enough), Evaluated(0.85, extra, 2.0, Enough)));
    }

    [Theory(DisplayName = "a better candidate is not put in use on fewer than ten confirmed recordings or a hundred boundaries")]
    [InlineData(9, 148)]
    [InlineData(12, 99)]
    public void TooLittleEvidenceKeepsTheVersionInUse(int recordings, int boundaries)
    {
        EvaluationEvidence little = new(recordings, boundaries, Made);

        Assert.Equal(AdoptionVerdict.TooLittleEvidence, SegmentAdoption.Judge(InUse, Evaluated(0.5, 3.0, 9.0, little), Evaluated(0.99, 0.1, 0.5, little)));
        Assert.Equal(AdoptionVerdict.TooLittleEvidence, SegmentAdoption.Judge(InUse, new SegmentEvaluation(null, [], Enough, null), new SegmentEvaluation(null, [], Enough, null)));
    }

    [Fact(DisplayName = "a version chosen by hand is never replaced, however much better the candidate is")]
    public void AVersionChosenByHandStays()
    {
        Assert.Equal(AdoptionVerdict.ChosenByHand, SegmentAdoption.Judge(ChosenByHand, Evaluated(0.5, 3.0, 9.0, Enough), Evaluated(0.99, 0.1, 0.5, Enough)));
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

    [Fact(DisplayName = "a version chosen by hand does not go back, and nothing is compared with a version before it nobody skipped with")]
    public void AVersionChosenByHandOrWithoutAComparisonStays()
    {
        ViewerAction[] returning = [.. Actions(2, Adopted.AddDays(1), 30, 30)];

        Assert.Null(SegmentRollback.Judge(ChosenByHand, Before, [.. returning, .. Actions(1, Adopted.AddDays(-3), 40, 0)], Adopted.AddDays(2)));
        Assert.Null(SegmentRollback.Judge(InUse, Before, returning, Adopted.AddDays(2)));
        Assert.Throws<ArgumentException>(() => SegmentRollback.Judge(Before, InUse, returning, Adopted.AddDays(2)));
    }

    private static SegmentEvaluation Evaluated(double recall, double extra, double skipped, EvaluationEvidence evidence)
        => new(new SegmentMetrics(recall, recall - 0.1, extra, skipped, [new KindMetrics(SegmentKind.Break, 0.9, 0.9)], null, 0), [], evidence, 0.7);

    private static IEnumerable<ViewerAction> Actions(int version, DateTime from, int skips, int returned)
        => Enumerable.Range(0, skips).Select(at => Action(ViewerActionKind.Skipped, version, from.AddMinutes(at)))
            .Concat(Enumerable.Range(0, returned).Select(at => Action(ViewerActionKind.Returned, version, from.AddMinutes(at).AddSeconds(30))));

    private static ViewerAction Action(ViewerActionKind action, int version, DateTime at)
        => new(at, action, SegmentKind.Break, TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(690), version);
}
