using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Segments;

public sealed class SegmentFiguresDomainServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Now = new(2026, 11, 1, 18, 30, 0, DateTimeKind.Utc);

    private static readonly SegmentMetrics Own = new(0.9, 0.8, [new KindMetrics(SegmentKind.Break, 0.9)], null, 0);

    private static readonly SegmentMetrics Measured = new(0.7, 0.6, [new KindMetrics(SegmentKind.Break, 0.8)], null, 0);

    private static readonly EvaluationEvidence Evidence = new(12, 148, Now.AddHours(-6));

    private readonly HeldSegmentLearningRounds rounds = new();

    private readonly HeldSkipEvents skips = new();

    private readonly HeldCatalogEvidence catalog = new() { Evidence = new EvaluationEvidence(3, 9, null) };

    private SegmentFiguresDomainService Service => new(rounds, skips, catalog, new HandTurnedClock(new DateTimeOffset(Now)));

    [Fact(DisplayName = "BR-CD-008: a version in use evaluated itself is shown by its own figures, beside what viewers did with its buttons over the last seven days")]
    public async Task AnEvaluatedVersionShowsItsOwnFigures()
    {
        SegmentModelVersion active = Version(2, new SegmentEvaluation(Own, [], Evidence));
        await rounds.TakeAsync(Round(active.Number), Cancel);
        await SkippedAsync(2, Now.AddDays(-1), ViewerActionKind.Skipped, 4);
        await SkippedAsync(2, Now.AddDays(-1), ViewerActionKind.Returned, 1);
        await SkippedAsync(2, Now.AddDays(-8), ViewerActionKind.Returned, 3);

        Assert.Equal(Shown(Own.WithReturns(new SkipReturns(4, 1))), Shown(await Service.InUseAsync(active, Cancel)));
    }

    [Fact(DisplayName = "BR-CD-008: a version in use never evaluated itself is shown by the figures the latest round that measured it took, and by none when no round measured it")]
    public async Task AnUnevaluatedVersionShowsTheRoundsFigures()
    {
        SegmentModelVersion active = Version(2, null);

        Assert.Null(await Service.InUseAsync(active, Cancel));

        await rounds.TakeAsync(Round(3), Cancel);

        Assert.Null(await Service.InUseAsync(active, Cancel));

        await rounds.TakeAsync(Round(2, day: 1), Cancel);

        Assert.Equal(Shown(Measured.WithReturns(new SkipReturns(0, 0))), Shown(await Service.InUseAsync(active, Cancel)));
    }

    [Fact(DisplayName = "BR-CD-008: what the latest evaluation stands on is that of the highest numbered version evaluated, and what the catalog would give one while none was")]
    public async Task TheEvidenceIsTheLatestEvaluations()
    {
        EvaluationEvidence older = new(5, 50, Now.AddDays(-3));

        Assert.Equal(catalog.Evidence, await Service.EvidenceAsync([Version(1, null)], Cancel));
        Assert.Equal(
            Evidence,
            await Service.EvidenceAsync([Version(2, new SegmentEvaluation(Own, [], older)), Version(3, new SegmentEvaluation(Own, [], Evidence)), Version(4, null)], Cancel));
        Assert.Equal(1, catalog.Reads);
    }

    private static (double?, double?, int?, double?) Shown(SegmentMetrics? metrics)
        => (metrics?.BoundaryRecallWithin2s, metrics?.ReturnedAfterSkipShare, metrics?.Skips, metrics?.BreakRecall);

    private static SegmentModelVersion Version(int number, SegmentEvaluation? evaluation)
        => SegmentModelVersion.Rehydrate(number, SegmentModelVersionState.Active, false, HandMadeModels.First, Now.AddDays(-5), Now.AddDays(-4), null, null, null, evaluation);

    private static SegmentLearningRound Round(int inUseVersion, int day = 0)
        => SegmentLearningRound.Ended(
            SegmentLearningRound.DayOf(Now.AddDays(-3 + day)),
            Now.AddDays(-3 + day),
            SegmentTrainingOutcome.Kept,
            9,
            AdoptionVerdict.NoBetterAtBoundaries,
            Measured,
            inUseVersion);

    private async Task SkippedAsync(int version, DateTime from, ViewerActionKind action, int count)
    {
        for (int at = 0; at < count; at++)
        {
            await skips.TakeAsync(
                SkipEvent.Of(RecordingId.New(), new ViewerAction(from.AddMinutes(at), action, SegmentKind.Break, TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(360), version)),
                Cancel);
        }
    }
}
