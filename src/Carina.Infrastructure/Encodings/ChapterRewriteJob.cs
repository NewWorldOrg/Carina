using Carina.Domain.Encodings;
using Carina.Domain.Segments;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Encodings;

public sealed record ChapterRewriteSettings
{
    public static readonly ChapterRewriteSettings Default = new();

    public TimeSpan BeforeFirstLook { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan BetweenLooks { get; init; } = TimeSpan.FromMinutes(10);

    public int AtMostALook { get; init; } = 4;
}

public sealed record ChapterRewriteLook(SpareTimeVerdict Verdict, ChapterRewriteRound? Round);

public sealed class ChapterRewriteJob(
    IServiceScopeFactory scopes,
    ChapterRewriteSettings settings,
    TimeProvider clock,
    ILogger<ChapterRewriteJob> logger) : BackgroundService
{
    private readonly Lock gate = new();

    private readonly Dictionary<EncodeJobId, DateTime> failedAt = [];

    public async Task<ChapterRewriteLook> LookAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        IServiceProvider services = scope.ServiceProvider;
        IOccupancyReader occupancy = services.GetRequiredService<IOccupancyReader>();
        SpareTimeVerdict verdict = await JudgedAsync(occupancy, cancellationToken);

        if (verdict is not SpareTimeVerdict.Spare)
        {
            return new ChapterRewriteLook(verdict, null);
        }

        SegmentModelVersion version = await services.GetRequiredService<ISegmentModelVersionRepository>().ActiveAsync(cancellationToken);
        ChapterRewriteRound round = await services.GetRequiredService<ArtefactChapterRewrites>().RewriteAsync(
            version.Number,
            settings.AtMostALook,
            PassedOver(),
            async token => await JudgedAsync(occupancy, token) is SpareTimeVerdict.Spare,
            cancellationToken);

        Remember(round.Failed);

        return new ChapterRewriteLook(verdict, round);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan waiting = settings.BeforeFirstLook;

        while (await WaitAsync(waiting, stoppingToken))
        {
            waiting = settings.BetweenLooks;

            try
            {
                Told(await LookAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception failure)
            {
                logger.LogError(failure, "A look at the chapters of standing artefacts failed; the next one is unaffected.");
            }
        }
    }

    private async Task<SpareTimeVerdict> JudgedAsync(IOccupancyReader occupancy, CancellationToken cancellationToken)
    {
        DateTime now = Now();

        return SpareTime.Judge(await occupancy.ReadAsync(now, cancellationToken), now);
    }

    private IReadOnlyCollection<EncodeJobId> PassedOver()
    {
        DateTime since = Now() - ChapterRewrite.OncePer;

        lock (gate)
        {
            return [.. failedAt.Where(failed => failed.Value > since).Select(failed => failed.Key)];
        }
    }

    private void Remember(IReadOnlyList<EncodeJobId> failed)
    {
        DateTime now = Now();

        lock (gate)
        {
            foreach (EncodeJobId job in failed)
            {
                failedAt[job] = now;
            }
        }
    }

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

    private void Told(ChapterRewriteLook look)
    {
        if (look.Round is not { Read: > 0 } round)
        {
            return;
        }

        logger.LogInformation(
            "A look at the chapters of standing artefacts read {Read}: {Rewritten} written again, {AsCarried} as they were, {PassedOver} passed over, {Abandoned} dropped for later, {Failed} failed{Yielded}.",
            round.Read,
            round.Rewritten,
            round.AsCarried,
            round.PassedOver,
            round.Abandoned,
            round.Failed.Count,
            round.Yielded ? ", giving way to the machine being needed" : string.Empty);
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
}
