using Carina.Domain.Segments;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Segments;

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

public sealed record SegmentTrainingLook(SpareTimeVerdict? Verdict, SegmentTrainingRound? Round);

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

        if (!await holding.ServiceProvider.GetRequiredService<SegmentLearningRoundDomainService>().IsDueAsync(day, cancellationToken))
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

        if (turn is null || !await services.GetRequiredService<SegmentLearningRoundDomainService>().IsDueAsync(day, cancellationToken))
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

        if (SegmentLearningRound.IsKept(made.Outcome))
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
        SegmentVersionDomainService versions = services.GetRequiredService<SegmentVersionDomainService>();
        (SegmentRollback? rollback, SegmentModelVersion active) = await versions.RollBackWhenDueAsync(started, cancellationToken);

        if (!await services.GetRequiredService<SegmentLearningRoundDomainService>().HasNewsAsync(cancellationToken))
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

        if (trained.Learned is not { IsKept: true } learned)
        {
            return new SegmentTrainingRound(day, rollback, SegmentTrainingOutcome.NothingToLearnFrom, null, null, active);
        }

        (SegmentModelVersion candidate, IReadOnlyList<int> letGo, AdoptionVerdict verdict) = await versions.KeepCandidateAsync(learned, cancellationToken);

        SegmentTrainingRound decided = verdict switch
        {
            AdoptionVerdict.Adopted => await AdoptAsync(versions, day, rollback, candidate, active, cancellationToken),
            AdoptionVerdict.HeldAfterRollback => new SegmentTrainingRound(day, rollback, SegmentTrainingOutcome.HeldAfterRollback, candidate, verdict, active),
            _ => new SegmentTrainingRound(day, rollback, SegmentTrainingOutcome.Kept, candidate, verdict, active),
        };

        return decided with { InUseMetrics = learned.InUse.Metrics, InUseVersion = active.Number, LetGo = letGo };
    }

    private static async Task<SegmentTrainingRound> AdoptAsync(
        SegmentVersionDomainService versions,
        DateOnly day,
        SegmentRollback? rollback,
        SegmentModelVersion candidate,
        SegmentModelVersion judgedAgainst,
        CancellationToken cancellationToken)
    {
        (bool adopted, SegmentModelVersion inUse) = await versions.AdoptAsync(candidate, judgedAgainst, cancellationToken);
        SegmentTrainingOutcome outcome = adopted ? SegmentTrainingOutcome.Adopted : SegmentTrainingOutcome.InUseChangedMeanwhile;

        return new SegmentTrainingRound(day, rollback, outcome, candidate, AdoptionVerdict.Adopted, inUse);
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
