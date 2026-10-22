using Carina.Api.Common;
using Carina.Api.Responder.Segments;
using Carina.Api.Services;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.TestSupport;

namespace Carina.Api.Tests.Unit;

public sealed class SegmentStatusServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Taken = new(2026, 11, 7, 18, 6, 0, DateTimeKind.Utc);

    private readonly HeldSegmentModelVersions versions = new();

    private readonly HeldSegmentLearningRounds rounds = new();

    private readonly HeldSkipEvents skips = new();

    [Fact(DisplayName = "BR-CD-008: a version in use evaluated itself keeps its own figures, and the figures of a round that measured it are not taken in their place")]
    public async Task AVersionEvaluatedItselfKeepsItsOwnFigures()
    {
        versions.Active = SegmentModelVersion.Rehydrate(
            2,
            SegmentModelVersionState.Active,
            false,
            SegmentModel.First,
            Taken.AddDays(-3),
            Taken.AddDays(-3),
            null,
            null,
            null,
            new SegmentEvaluation(new SegmentMetrics(0.93, 0.85, [new KindMetrics(SegmentKind.Break, 0.95)], null, 0), [], new EvaluationEvidence(12, 150, null)));
        await TakeRoundAsync(InUseFigures(), 2);

        SegmentStatus status = await ReadAsync();

        Assert.Equal(0.93, status.Metrics?.BoundaryRecallWithin2s);
        Assert.Equal(0.95, status.Metrics?.BreakRecall);
        Assert.Equal(0.93, StandingOf(status, 2));
    }

    [Fact(DisplayName = "BR-CD-008: a version in use that was never evaluated itself answers the figures a round measured it by, beside what viewers did with its buttons, in its history row as well")]
    public async Task AVersionNeverEvaluatedAnswersTheFiguresOfTheRoundThatMeasuredIt()
    {
        await TakeRoundAsync(InUseFigures(), SegmentModelVersion.FirstNumber);
        await SkippedAsync(SegmentModelVersion.FirstNumber, ViewerActionKind.Skipped, 8);
        await SkippedAsync(SegmentModelVersion.FirstNumber, ViewerActionKind.Returned, 2);

        SegmentStatus status = await ReadAsync();

        Assert.NotNull(status.Metrics);
        Assert.Equal((0.08, 0.05, 0.38), (status.Metrics.BoundaryRecallWithin2s, status.Metrics.BoundaryRecallWithin1s, status.Metrics.BreakRecall));
        Assert.Equal((2 / 8d, 8), (status.Metrics.ReturnedAfterSkipShare, status.Metrics.Skips));
        Assert.Equal(0.08, StandingOf(status, SegmentModelVersion.FirstNumber));
    }

    [Fact(DisplayName = "BR-CD-008: a version in use that was never evaluated itself keeps the figures of the latest round that measured it when later rounds learned nothing and kept none, while the last round stays the latest")]
    public async Task AVersionNeverEvaluatedKeepsTheFiguresOfAnOlderRoundWhenLaterRoundsMeasuredNone()
    {
        await TakeRoundAsync(InUseFigures(), SegmentModelVersion.FirstNumber);
        await TakeWithoutFiguresAsync(Taken.AddDays(1), SegmentTrainingOutcome.NothingNew);
        await TakeWithoutFiguresAsync(Taken.AddDays(2), SegmentTrainingOutcome.NothingToLearnFrom);
        await TakeWithoutFiguresAsync(Taken.AddDays(3), SegmentTrainingOutcome.Failed);

        SegmentStatus status = await ReadAsync();

        Assert.Equal(SegmentTrainingOutcome.Failed, status.LastRound?.Outcome);
        Assert.NotNull(status.Metrics);
        Assert.Equal((0.08, 0.38), (status.Metrics.BoundaryRecallWithin2s, status.Metrics.BreakRecall));
        Assert.Equal(0.08, StandingOf(status, SegmentModelVersion.FirstNumber));
    }

    [Fact(DisplayName = "BR-CD-008: a version in use that was never evaluated itself takes the figures of the latest round that measured it, not of a later round that measured another version")]
    public async Task AVersionNeverEvaluatedTakesTheLatestRoundThatMeasuredItAndNotAnother()
    {
        await TakeRoundAsync(InUseFigures(), SegmentModelVersion.FirstNumber);
        await TakeRoundAsync(new SegmentMetrics(0.61, 0.5, [new KindMetrics(SegmentKind.Break, 0.7)], null, 0), 2, Taken.AddDays(1));

        SegmentStatus status = await ReadAsync();

        Assert.Equal(0.08, status.Metrics?.BoundaryRecallWithin2s);
        Assert.Equal(0.08, StandingOf(status, SegmentModelVersion.FirstNumber));
    }

    [Fact(DisplayName = "BR-CD-008: a version in use that was put in use after a round was taken still answers the figures of that round when it measured that version, as after a rollback made within the round")]
    public async Task AVersionPutInUseAfterTheRoundWasTakenAnswersTheFiguresOfTheRoundThatMeasuredIt()
    {
        DateTime rolledBack = Taken.AddSeconds(30);
        versions.Active = SegmentModelVersion.Rehydrate(1, SegmentModelVersionState.Active, false, SegmentModel.First, HeldSegmentModelVersions.Installed, rolledBack, null, null, null);
        versions.Others.Add(SegmentModelVersion.Rehydrate(2, SegmentModelVersionState.Retired, false, SegmentModel.First, Taken.AddDays(-3), Taken.AddDays(-3), rolledBack, null, rolledBack, null, new SegmentRollback(0.4, 0.1, 25, 1)));
        await TakeRoundAsync(InUseFigures(), SegmentModelVersion.FirstNumber);

        SegmentStatus status = await ReadAsync();

        Assert.NotNull(status.Metrics);
        Assert.Equal((0.08, 0.38), (status.Metrics.BoundaryRecallWithin2s, status.Metrics.BreakRecall));
        Assert.Equal(0.08, StandingOf(status, SegmentModelVersion.FirstNumber));
    }

    [Fact(DisplayName = "BR-CD-008: a version in use that was never evaluated itself has no figures when the rounds measured another version")]
    public async Task AVersionNeverEvaluatedHasNoFiguresWhenTheRoundsMeasuredAnother()
    {
        versions.Active = HeldSegmentModelVersions.InUse(3, Taken.AddHours(2));
        versions.Others.Add(SegmentModelVersion.Rehydrate(1, SegmentModelVersionState.Retired, false, SegmentModel.First, HeldSegmentModelVersions.Installed, HeldSegmentModelVersions.Installed, Taken.AddHours(2), null, null));
        await TakeRoundAsync(InUseFigures(), SegmentModelVersion.FirstNumber);
        await TakeWithoutFiguresAsync(Taken.AddDays(1), SegmentTrainingOutcome.NothingNew);
        await SkippedAsync(3, ViewerActionKind.Skipped, 4);

        SegmentStatus status = await ReadAsync();

        Assert.Null(status.Metrics);
        Assert.Null(StandingOf(status, 3));
    }

    [Fact(DisplayName = "BR-CD-008: a version in use that was never evaluated itself has no figures from a round kept before the version it measured was kept")]
    public async Task AVersionNeverEvaluatedHasNoFiguresFromARoundThatKeptNoVersionNumber()
    {
        await rounds.TakeAsync(
            SegmentLearningRound.Ended(SegmentLearningRound.DayOf(Taken), Taken, SegmentTrainingOutcome.Kept, 9, AdoptionVerdict.TooLittleEvidence, InUseFigures()),
            CancellationToken.None);

        SegmentStatus status = await ReadAsync();

        Assert.Null(status.Metrics);
        Assert.Null(StandingOf(status, SegmentModelVersion.FirstNumber));
    }

    [Fact(DisplayName = "BR-CD-008: a version in use that was never evaluated itself has no figures when no round measured any")]
    public async Task AVersionNeverEvaluatedHasNoFiguresWhenNoRoundMeasuredAny()
    {
        await TakeWithoutFiguresAsync(Taken, SegmentTrainingOutcome.NothingNew);

        SegmentStatus status = await ReadAsync();

        Assert.Null(status.Metrics);
        Assert.Null(StandingOf(status, SegmentModelVersion.FirstNumber));
    }

    [Fact(DisplayName = "BR-CD-008: a version in use that was never evaluated itself has no figures before any round was taken")]
    public async Task AVersionNeverEvaluatedHasNoFiguresBeforeAnyRound()
    {
        SegmentStatus status = await ReadAsync();

        Assert.Null(status.Metrics);
        Assert.Null(StandingOf(status, SegmentModelVersion.FirstNumber));
    }

    private static SegmentMetrics InUseFigures()
        => new(0.08, 0.05, [new KindMetrics(SegmentKind.Break, 0.38)], null, 0);

    private static double? StandingOf(SegmentStatus status, int number)
        => SegmentStatusResponder.Of(status).Versions.Single(version => version.Number == number).Metrics?.BoundaryRecallWithin2s;

    private async Task TakeRoundAsync(SegmentMetrics inUse, int measured, DateTime? at = null)
        => await rounds.TakeAsync(
            SegmentLearningRound.Ended(SegmentLearningRound.DayOf(at ?? Taken), at ?? Taken, SegmentTrainingOutcome.Kept, 9, AdoptionVerdict.TooLittleEvidence, inUse, measured),
            CancellationToken.None);

    private async Task TakeWithoutFiguresAsync(DateTime at, SegmentTrainingOutcome outcome)
        => await rounds.TakeAsync(SegmentLearningRound.Ended(SegmentLearningRound.DayOf(at), at, outcome), CancellationToken.None);

    private async Task SkippedAsync(int version, ViewerActionKind action, int count)
    {
        foreach (int index in Enumerable.Range(0, count))
        {
            await skips.TakeAsync(
                SkipEvent.Of(RecordingId.New(), new ViewerAction(Now.AddDays(-1).AddMinutes(index), action, SegmentKind.Break, TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(360), version)),
                CancellationToken.None);
        }
    }

    private async Task<SegmentStatus> ReadAsync()
    {
        SegmentStatusService service = new(
            new HeldLearningDataAmount(),
            new HeldCatalogCounts(),
            versions,
            new HeldSegmentSettings(),
            skips,
            new HeldCatalogEvidence(),
            rounds,
            new HandTurnedClock(new DateTimeOffset(Now)));

        ServiceResult<SegmentStatus> result = await service.ReadAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);

        return result.Data!;
    }
}
