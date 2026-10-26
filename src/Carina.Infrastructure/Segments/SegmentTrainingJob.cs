using Carina.Domain.Segments;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Segments;

/// <summary>
/// What the day's round did: the rollback made before learning, if one was; what became of the candidate; the
/// candidate kept, what judging it against the version in use said, the figures of the version in use on the
/// same recordings with its number, and the candidates before it let go, when one was learned; the version in use
/// after the round; and, when the round stopped partway, what stood in the way.
/// </summary>
public sealed record SegmentTrainingRound(
    DateOnly Day,
    SegmentRollback? Rollback,
    SegmentTrainingOutcome Outcome,
    SegmentModelVersion? Candidate,
    AdoptionVerdict? Verdict,
    SegmentModelVersion Active)
{
    public SpareTimeVerdict? StoppedBy { get; init; }

    public SegmentMetrics? InUseMetrics { get; init; }

    public int? InUseVersion { get; init; }

    public IReadOnlyList<int> LetGo { get; init; } = [];
}

/// <summary>
/// What one look did: what in spare time stood in the way when no round was made, null when nothing was asked of
/// spare time because the day's round had been finished or another process was taking it; and the round when one
/// was made.
/// </summary>
public sealed record SegmentTrainingLook(SpareTimeVerdict? Verdict, SegmentTrainingRound? Round);

/// <summary>
/// Takes the day's round of learning (<see cref="SegmentLearningRound"/>) once a day, in <see cref="SpareTime"/>
/// once the day has begun, so nothing of it happens while anything is recorded or watched, or just before a
/// reservation. Spare time is asked again after each recording learning reads and after each step it
/// takes; when it has ended, the round stops there, throws what it learned away, and is taken again at the next look
/// in spare time the same day. A round is kept as finished once it ends otherwise, failing included, so it is not
/// taken again that day; one process takes a round at a time. A round first rolls the version in use back when
/// <see cref="SegmentRollback"/> says so; then, when <see cref="SegmentLearningRound.HasNews"/> finds something
/// learning reads changed since the last round that kept a candidate, learns a candidate beside the version in use
/// and keeps it as a candidate, unless no recording was judged, when there was nothing to learn from; lets go of the
/// candidates before it that were never used (<see cref="ISegmentModelVersionRepository.LetGoOfCandidatesBeforeAsync"/>),
/// the round keeping the candidate's figures so that what it says outlasts it; and puts it in
/// use when <see cref="SegmentAdoption"/> says so, no version was rolled
/// back from in the days <see cref="SegmentAdoption.AfterRollbacks"/> holds candidates out, and the version in use
/// is still the one it was judged against. The version in use moves only through
/// <see cref="ISegmentModelVersionRepository.ChangeAsync"/>, after which detection in spare time detects the
/// recordings still kept again with it. Spare time is looked for after
/// <see cref="SegmentTrainingSettings.BeforeFirstLook"/>, then every <see cref="SegmentTrainingSettings.BetweenLooks"/>.
/// </summary>
public sealed class SegmentTrainingJob(
    IServiceScopeFactory scopes,
    SegmentTrainingSettings settings,
    TimeProvider clock,
    ILogger<SegmentTrainingJob> logger) : BackgroundService
{
    public async Task<SegmentTrainingLook> LookAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope holding = scopes.CreateAsyncScope();
        ISegmentLearningRoundRepository turns = holding.ServiceProvider.GetRequiredService<ISegmentLearningRoundRepository>();
        DateTime now = Now();
        DateOnly day = SegmentLearningRound.DayOf(now);

        if (await turns.TakenAsync(day, cancellationToken))
        {
            return new SegmentTrainingLook(null, null);
        }

        SpareTimeVerdict verdict = await SpareAsync(holding.ServiceProvider, cancellationToken);

        if (verdict is not SpareTimeVerdict.Spare)
        {
            return new SegmentTrainingLook(verdict, null);
        }

        await using IAsyncDisposable? turn = await turns.HoldAsync(cancellationToken);
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        IServiceProvider services = scope.ServiceProvider;
        ISegmentLearningRoundRepository rounds = services.GetRequiredService<ISegmentLearningRoundRepository>();

        if (turn is null || await rounds.TakenAsync(day, cancellationToken))
        {
            return new SegmentTrainingLook(null, null);
        }

        SegmentTrainingRound made = await FinishedAsync(services, rounds, day, now, cancellationToken);

        Told(made);

        return new SegmentTrainingLook(verdict, made);
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

    private async Task<SpareTimeVerdict> SpareAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        DateTime now = Now();

        return SpareTime.Judge(await services.GetRequiredService<IOccupancyReader>().ReadAsync(now, cancellationToken), now);
    }

    /// <summary>
    /// Makes the round and keeps it as finished, with what became of it, the candidate it kept, the verdict on it, the
    /// figures of the version in use it was judged against and its number, and the candidate's own figures and what
    /// they stand on, unless it stopped partway; a round that fails is kept as finished too, as failed, so that the
    /// next try is the next day's.
    /// </summary>
    private async Task<SegmentTrainingRound> FinishedAsync(
        IServiceProvider services,
        ISegmentLearningRoundRepository rounds,
        DateOnly day,
        DateTime started,
        CancellationToken cancellationToken)
    {
        SegmentTrainingRound made;

        try
        {
            made = await RoundAsync(services, day, started, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            await KeepFailedAsync(rounds, day, started);

            throw;
        }

        if (made.Outcome is not SegmentTrainingOutcome.Stopped)
        {
            await rounds.TakeAsync(
                SegmentLearningRound.Ended(
                    day,
                    started,
                    made.Outcome,
                    made.Candidate?.Number,
                    made.Verdict,
                    made.InUseMetrics,
                    made.InUseVersion,
                    made.Candidate?.Metrics,
                    made.Candidate?.Evidence),
                cancellationToken);
        }

        return made;
    }

    private async Task KeepFailedAsync(ISegmentLearningRoundRepository rounds, DateOnly day, DateTime started)
    {
        try
        {
            await rounds.TakeAsync(SegmentLearningRound.Ended(day, started, SegmentTrainingOutcome.Failed), CancellationToken.None);
        }
        catch (Exception failure)
        {
            logger.LogError(failure, "The failed round of learning of {Day} could not be kept as finished; it is taken again at the next look.", day);
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

        if (!SegmentLearningRound.HasNews(
                await services.GetRequiredService<ISegmentLearningRoundRepository>().LastLearnedAsync(cancellationToken),
                await services.GetRequiredService<ILearningMaterialChanges>().LastChangedAsync(cancellationToken)))
        {
            return new SegmentTrainingRound(day, rollback, SegmentTrainingOutcome.NothingNew, null, null, active);
        }

        SpareTimeVerdict stoppedBy = SpareTimeVerdict.Spare;

        async Task<bool> MayGoOnAsync(CancellationToken token)
        {
            stoppedBy = await SpareAsync(services, token);

            return stoppedBy is SpareTimeVerdict.Spare;
        }

        Trained trained = await services.GetRequiredService<ISegmentTrainer>().TrainAsync(active, started, MayGoOnAsync, cancellationToken);

        if (trained.Stopped)
        {
            return new SegmentTrainingRound(day, rollback, SegmentTrainingOutcome.Stopped, null, null, active) { StoppedBy = stoppedBy };
        }

        if (trained.Learned is not { } learned || learned.Evaluation.Evidence.Recordings is 0)
        {
            return new SegmentTrainingRound(day, rollback, SegmentTrainingOutcome.NothingToLearnFrom, null, null, active);
        }

        SegmentModelVersion candidate = await versions.AddCandidateAsync(learned.Candidate, learned.Evaluation, Now(), cancellationToken);
        IReadOnlyList<int> letGo = await versions.LetGoOfCandidatesBeforeAsync(candidate.Number, cancellationToken);
        AdoptionVerdict verdict = SegmentAdoption.AfterRollbacks(learned.Verdict, await versions.ListAsync(cancellationToken), Now());

        SegmentTrainingRound decided = verdict switch
        {
            AdoptionVerdict.Adopted => await AdoptAsync(versions, day, rollback, candidate, active, cancellationToken),
            AdoptionVerdict.HeldAfterRollback => new SegmentTrainingRound(day, rollback, SegmentTrainingOutcome.HeldAfterRollback, candidate, verdict, active),
            _ => new SegmentTrainingRound(day, rollback, SegmentTrainingOutcome.Kept, candidate, verdict, active),
        };

        return decided with { InUseMetrics = learned.InUse.Metrics, InUseVersion = active.Number, LetGo = letGo };
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
            await LookAsync(stoppingToken);
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

    private void Told(SegmentTrainingRound round)
    {
        if (round.Rollback is { } rollback)
        {
            logger.LogWarning(
                "The version in use went back to version {Version}: viewers came back from {Share:P1} of {Skips} skips, against {Previous:P1} before it.",
                rollback.BackTo,
                rollback.ReturnedAfterSkipShare,
                rollback.Skips,
                rollback.PreviousShare);
        }

        switch (round.Outcome)
        {
            case SegmentTrainingOutcome.NothingNew:
                logger.LogInformation(
                    "The round of learning of {Day} learned nothing: nothing it reads has changed since the last round that kept a candidate.",
                    round.Day);
                break;

            case SegmentTrainingOutcome.Stopped:
                logger.LogInformation(
                    "The round of learning of {Day} stopped partway ({StoppedBy}) and threw what it learned away; it is taken again in spare time.",
                    round.Day,
                    round.StoppedBy);
                break;

            default:
                logger.LogInformation(
                    "The round of learning of {Day} ended {Outcome}: candidate {Candidate} ({Verdict}), version {Active} in use.",
                    round.Day,
                    round.Outcome,
                    round.Candidate?.Number,
                    round.Verdict,
                    round.Active.Number);
                break;
        }

        if (round.LetGo.Count is not 0)
        {
            logger.LogInformation(
                "The round of learning of {Day} let go of candidates {LetGo}, never used and not taken.",
                round.Day,
                string.Join(", ", round.LetGo));
        }
    }
}
