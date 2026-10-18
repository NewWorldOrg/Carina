using Carina.Domain.Segments;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Segments;

/// <summary>
/// What became of the day's round of learning.
/// </summary>
public enum SegmentTrainingOutcome
{
    NothingToLearnFrom = 1,

    Kept = 2,

    Adopted = 3,

    LearningSwitchedOff = 4,

    InUseChangedMeanwhile = 5,
}

/// <summary>
/// What the day's round did: the rollback made before learning, if one was; what became of the candidate; the
/// candidate kept, and what judging it against the version in use said, when one was learned; and the version in
/// use after the round.
/// </summary>
public sealed record SegmentTrainingRound(
    DateOnly Day,
    SegmentRollback? Rollback,
    SegmentTrainingOutcome Outcome,
    SegmentModelVersion? Candidate,
    AdoptionVerdict? Verdict,
    SegmentModelVersion Active);

/// <summary>
/// What one look did: what stood in the way when no round was made — spare time, or learning being off; null when
/// nothing was asked of spare time because the day's round had been taken — and the round when one was made.
/// </summary>
public sealed record SegmentTrainingLook(SpareTimeVerdict? Verdict, SegmentTrainingRound? Round);

/// <summary>
/// Takes the day's round of learning (<see cref="SegmentLearningRound"/>) once a day, at the first look in
/// <see cref="SpareTime"/> once the day has begun, so nothing of it happens while learning is off, while anything
/// is recorded or watched, or just before a reservation. A round is kept as taken before it starts, so it is not
/// taken again that day, even after a failure or after the process starts again. A round first rolls the version
/// in use back when <see cref="SegmentRollback"/> says so; then learns a candidate beside the version in use and
/// keeps it as a candidate; and puts it in use when <see cref="SegmentAdoption"/> says so and the version in use
/// is still the one it was judged against. A candidate learned after learning was switched off is not kept. The
/// version in use moves only through <see cref="ISegmentModelVersionRepository.ChangeAsync"/>, after which
/// detection in spare time detects the recordings still kept again with it. Spare time is looked for after
/// <see cref="SegmentTrainingSettings.BeforeFirstLook"/>, then every
/// <see cref="SegmentTrainingSettings.BetweenLooks"/>.
/// </summary>
public sealed class SegmentTrainingJob(
    IServiceScopeFactory scopes,
    SegmentTrainingSettings settings,
    TimeProvider clock,
    ILogger<SegmentTrainingJob> logger) : BackgroundService
{
    public async Task<SegmentTrainingLook> LookAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        IServiceProvider services = scope.ServiceProvider;
        ISegmentLearningRoundRepository rounds = services.GetRequiredService<ISegmentLearningRoundRepository>();
        DateTime now = Now();
        SegmentLearningRound round = SegmentLearningRound.Taken(now);

        if (await rounds.TakenAsync(round.Day, cancellationToken))
        {
            return new SegmentTrainingLook(null, null);
        }

        SpareTimeVerdict verdict = SpareTime.Judge(await services.GetRequiredService<IOccupancyReader>().ReadAsync(now, cancellationToken), now);

        if (verdict is not SpareTimeVerdict.Spare || !await rounds.TakeAsync(round, cancellationToken))
        {
            return new SegmentTrainingLook(verdict is SpareTimeVerdict.Spare ? null : verdict, null);
        }

        return new SegmentTrainingLook(verdict, await RoundAsync(services, round.Day, now, cancellationToken));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan waiting = settings.BeforeFirstLook;

        while (await WaitAsync(waiting, stoppingToken))
        {
            waiting = settings.BetweenLooks;

            await OnceAsync(stoppingToken);
        }
    }

    private async Task<SegmentTrainingRound> RoundAsync(IServiceProvider services, DateOnly day, DateTime started, CancellationToken cancellationToken)
    {
        ISegmentModelVersionRepository versions = services.GetRequiredService<ISegmentModelVersionRepository>();
        SegmentRollback? rollback = await RollbackAsync(services, versions, started, cancellationToken);
        SegmentModelVersion active = rollback is null
            ? await versions.ActiveAsync(cancellationToken)
            : await versions.ChangeAsync(SegmentVersionChange.GoingBack(rollback), Now(), cancellationToken)
              ?? throw new InvalidOperationException("The version gone back to is no longer kept.");
        double threshold = SegmentSettingsStanding.Over(await services.GetRequiredService<ISegmentSettingsRepository>().ReadAsync(cancellationToken)).SkipThreshold;
        SegmentLearned? learned = await services.GetRequiredService<ISegmentTrainer>().TrainAsync(active, threshold, started, cancellationToken);

        if (learned is null)
        {
            return new SegmentTrainingRound(day, rollback, SegmentTrainingOutcome.NothingToLearnFrom, null, null, active);
        }

        if (!await services.GetRequiredService<ILearningSwitch>().IsOnAsync(cancellationToken))
        {
            return new SegmentTrainingRound(day, rollback, SegmentTrainingOutcome.LearningSwitchedOff, null, learned.Verdict, active);
        }

        SegmentModelVersion candidate = await versions.AddCandidateAsync(learned.Candidate, learned.Evaluation, Now(), cancellationToken);

        return learned.Verdict is AdoptionVerdict.Adopted
            ? await AdoptAsync(versions, day, rollback, candidate, active, cancellationToken)
            : new SegmentTrainingRound(day, rollback, SegmentTrainingOutcome.Kept, candidate, learned.Verdict, active);
    }

    /// <summary>
    /// Puts the candidate in use unless the version in use is no longer the one it was judged against, or has been
    /// chosen by hand since.
    /// </summary>
    private async Task<SegmentTrainingRound> AdoptAsync(
        ISegmentModelVersionRepository versions,
        DateOnly day,
        SegmentRollback? rollback,
        SegmentModelVersion candidate,
        SegmentModelVersion judgedAgainst,
        CancellationToken cancellationToken)
    {
        SegmentModelVersion inUse = await versions.ActiveAsync(cancellationToken);

        if (inUse.Number != judgedAgainst.Number || inUse.ByHand)
        {
            return new SegmentTrainingRound(day, rollback, SegmentTrainingOutcome.InUseChangedMeanwhile, candidate, AdoptionVerdict.Adopted, inUse);
        }

        SegmentModelVersion adopted = await versions.ChangeAsync(SegmentVersionChange.Adopting(candidate.Number), Now(), cancellationToken)
                                      ?? throw new InvalidOperationException("The candidate just kept is no longer kept.");

        return new SegmentTrainingRound(day, rollback, SegmentTrainingOutcome.Adopted, candidate, AdoptionVerdict.Adopted, adopted);
    }

    private static async Task<SegmentRollback?> RollbackAsync(
        IServiceProvider services,
        ISegmentModelVersionRepository versions,
        DateTime now,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SegmentModelVersion> kept = await versions.ListAsync(cancellationToken);
        SegmentModelVersion active = kept.Single(version => version.State is SegmentModelVersionState.Active);

        if (SegmentRollback.Previous(kept) is not { } previous || SegmentRollback.Spans(active, now) is not { } spans)
        {
            return null;
        }

        ISkipEventRepository skips = services.GetRequiredService<ISkipEventRepository>();
        SkipReturns since = await skips.ReturnsAsync(active.Number, spans.From, spans.Until, cancellationToken);
        SkipReturns before = await skips.ReturnsAsync(previous.Number, spans.BeforeFrom, spans.BeforeUntil, cancellationToken);

        return SegmentRollback.Judge(active, previous, since, before, now);
    }

    private async Task OnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            Told(await LookAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception failure)
        {
            logger.LogError(failure, "The day's round of learning failed; the round of the next day is unaffected.");
        }
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Waits for <paramref name="waiting"/>; false once the process is stopping.
    /// </summary>
    private async Task<bool> WaitAsync(TimeSpan waiting, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(waiting, clock, stoppingToken);

            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private void Told(SegmentTrainingLook look)
    {
        if (look.Round is not { } round)
        {
            return;
        }

        if (round.Rollback is { } rollback)
        {
            logger.LogWarning(
                "The version in use went back to version {Version}: viewers came back from {Share:P1} of {Skips} skips, against {Previous:P1} before it.",
                rollback.BackTo,
                rollback.ReturnedAfterSkipShare,
                rollback.Skips,
                rollback.PreviousShare);
        }

        logger.LogInformation(
            "The round of learning of {Day} ended {Outcome}: candidate {Candidate} ({Verdict}), version {Active} in use.",
            round.Day,
            round.Outcome,
            round.Candidate?.Number,
            round.Verdict,
            round.Active.Number);
    }
}
