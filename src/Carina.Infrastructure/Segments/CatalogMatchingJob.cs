using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Segments;

public sealed record CatalogMatchingLook(SpareTimeVerdict? Verdict, CatalogMatched? Matched, bool Marked, CatalogEntry? Reestimated = null);

public sealed class CatalogMatchingJob(
    IServiceScopeFactory scopes,
    LearningRecords records,
    FollowedToTheEnd followed,
    MatchedAtOnce matchedAtOnce,
    CatalogMatchingSettings settings,
    TimeProvider clock,
    ILogger<CatalogMatchingJob> logger) : BackgroundService
{
    public async Task<CatalogMatchingLook> MatchFollowedAsync(RecordingId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        IServiceProvider services = scope.ServiceProvider;

        if (await records.FindAsync(id, cancellationToken) is not { IsMatchedAtOnce: true } record)
        {
            return new CatalogMatchingLook(null, null, false);
        }

        CatalogMatchingLook look = await MatchAsync(services, record, null, cancellationToken);

        if (look.Marked)
        {
            matchedAtOnce.Tell(id);
        }

        return look;
    }

    public async Task<CatalogMatchingLook> LookAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        IServiceProvider services = scope.ServiceProvider;
        DateTime now = Now();
        SpareTimeVerdict verdict = SpareTime.Judge(await services.GetRequiredService<IOccupancyReader>().ReadAsync(now, cancellationToken), now);

        if (verdict is not SpareTimeVerdict.Spare)
        {
            return new CatalogMatchingLook(verdict, null, false);
        }

        if (await services.GetRequiredService<ICatalogWorklist>().NextAsync(cancellationToken) is { } next)
        {
            return await MatchAsync(services, next, verdict, cancellationToken);
        }

        return await services.GetRequiredService<ICatalogRepository>().NextToReestimateAsync(cancellationToken) is { } outdated
            ? await ReestimateAsync(services, outdated, verdict, cancellationToken)
            : new CatalogMatchingLook(verdict, null, false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan waiting = settings.BeforeFirstLook;

        while (await WaitAsync(waiting, stoppingToken))
        {
            if (followed.TryTake(out RecordingId? id))
            {
                await OnceAsync(token => MatchFollowedAsync(id, token), stoppingToken);

                continue;
            }

            CatalogMatchingLook? look = await OnceAsync(LookAsync, stoppingToken);

            waiting = look?.Matched is null && look?.Reestimated is null ? settings.BetweenLooks : settings.AfterAMatch;
        }
    }

    private async Task<CatalogMatchingLook> MatchAsync(
        IServiceProvider services,
        LearningExtraction record,
        SpareTimeVerdict? verdict,
        CancellationToken cancellationToken)
    {
        CatalogMatched matched = await services.GetRequiredService<CatalogMatching>().MatchAsync(record, settings.Around, Now(), cancellationToken);
        ICatalogRepository catalog = services.GetRequiredService<ICatalogRepository>();

        await catalog.KeepAsync(matched.Change, cancellationToken);

        ExtractionChange marked = await records.ChangeAsync(record.RecordingId, found => found.MatchedAsRead(record.UpdatedAt, Now()), cancellationToken);

        await HeardAnewAsync(services, matched.Change, cancellationToken);

        return new CatalogMatchingLook(verdict, matched, marked is ExtractionChange.Written);
    }

    private async Task<CatalogMatchingLook> ReestimateAsync(
        IServiceProvider services,
        CatalogEntry entry,
        SpareTimeVerdict verdict,
        CancellationToken cancellationToken)
    {
        CatalogChange change = await services.GetRequiredService<CatalogMatching>().ReestimateAsync(entry, Now(), cancellationToken);
        ICatalogRepository catalog = services.GetRequiredService<ICatalogRepository>();

        await catalog.KeepAsync(change, cancellationToken);
        await HeardAnewAsync(services, change, cancellationToken);

        return new CatalogMatchingLook(verdict, null, false, entry);
    }

    private async Task HeardAnewAsync(IServiceProvider services, CatalogChange change, CancellationToken cancellationToken)
    {
        IReadOnlyList<RecordingId> heard = await services.GetRequiredService<CatalogMatchingDomainService>().HeardAnewAsync(change, cancellationToken);
        DateTime now = Now();

        foreach (RecordingId recording in heard)
        {
            await records.ChangeAsync(recording, record => record.CatalogChanged(now), cancellationToken);
        }
    }

    private async Task<CatalogMatchingLook?> OnceAsync(Func<CancellationToken, Task<CatalogMatchingLook>> matching, CancellationToken stoppingToken)
    {
        try
        {
            CatalogMatchingLook look = await matching(stoppingToken);

            Told(look);

            return look;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception failure)
        {
            logger.LogError(failure, "A matching against the catalog failed; the next one is unaffected.");

            return null;
        }
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private async Task<bool> WaitAsync(TimeSpan waiting, CancellationToken stoppingToken)
    {
        using CancellationTokenSource either = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        Task elapsed = Task.Delay(waiting, clock, either.Token);
        Task told = followed.WaitAsync(either.Token);

        await Task.WhenAny(elapsed, told);
        await either.CancelAsync();
        await Task.WhenAll(elapsed, told).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        return !stoppingToken.IsCancellationRequested;
    }

    private void Told(CatalogMatchingLook look)
    {
        if (look.Reestimated is { } entry)
        {
            logger.LogInformation(
                "Catalog entry {Entry} was estimated again with version {Version} of the estimation, as {Kind}.",
                entry.Id.Value,
                entry.KindsVersion,
                entry.Kind);

            return;
        }

        if (look.Matched is not { } matched)
        {
            return;
        }

        logger.LogInformation(
            "Recording {Recording} was matched against the catalog {Path}: {Matches} match(es), {Started} entr(ies) started, "
            + "{Reestimated} estimated again and {Found} stretch(es) found; its record was {Marked}.",
            matched.Recording.Wire,
            look.Verdict is null ? "as soon as it was read to its end" : "in spare time",
            matched.Matches,
            matched.Change.Started.Count,
            matched.Change.Reestimated.Count,
            matched.Change.Found.Count,
            look.Marked ? "marked as matched" : "left waiting, having changed meanwhile");
    }
}
