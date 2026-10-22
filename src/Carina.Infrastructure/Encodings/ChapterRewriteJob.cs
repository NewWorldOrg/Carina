using Carina.Domain.Encodings;
using Carina.Domain.Segments;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// How long after the start standing artefacts are first compared with the segments, how often after that,
/// and how many artefacts one look takes at most.
/// </summary>
public sealed record ChapterRewriteSettings
{
    public static readonly ChapterRewriteSettings Default = new();

    public TimeSpan BeforeFirstLook { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan BetweenLooks { get; init; } = TimeSpan.FromMinutes(10);

    public int AtMostALook { get; init; } = 4;
}

/// <summary>
/// What one look did: what in spare time stood in the way when nothing was compared, and the round when one was
/// made.
/// </summary>
public sealed record ChapterRewriteLook(SpareTimeVerdict Verdict, ChapterRewriteRound? Round);

/// <summary>
/// Writes the chapters of standing artefacts again in <see cref="SpareTime"/>, so nothing of it happens while
/// anything is recorded or watched, or just before a reservation. Each look compares at
/// most <see cref="ChapterRewriteSettings.AtMostALook"/> artefacts with the detection of their recording by the
/// version in use, and gives way as soon as the machine is needed. An artefact that could not be written again
/// is passed over for a day, or until the process starts again.
/// </summary>
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
