using Carina.Domain.Segments;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Segments;

/// <summary>
/// What one look for a recording to match against the catalog did: what stood in the way of spare time
/// when it was not, what the matching it ran found and changed, and whether the record was marked as
/// matched.
/// </summary>
public sealed record CatalogMatchingLook(SpareTimeVerdict Verdict, CatalogMatched? Matched, bool Marked);

/// <summary>
/// Matches against the catalog, one at a time, the recordings whose learning data waits to be matched:
/// each recording once its data is read to the end or as far as it could be, the most recently started
/// first, whether it was read as it was recorded or afterwards. It looks after
/// <see cref="CatalogMatchingSettings.BeforeFirstLook"/>, then every
/// <see cref="CatalogMatchingSettings.BetweenLooks"/>, or after <see cref="CatalogMatchingSettings.AfterAMatch"/>
/// once it has matched one. Nothing begins outside <see cref="SpareTime"/>, so nothing runs while learning is
/// off, and a match finished after learning was switched off is not written. What a match changes is written
/// before the record is marked as matched; a record that changed meanwhile is left waiting and is matched
/// again, which adds no stretch twice.
/// </summary>
public sealed class CatalogMatchingJob(
    IServiceScopeFactory scopes,
    LearningRecords records,
    CatalogMatchingSettings settings,
    TimeProvider clock,
    ILogger<CatalogMatchingJob> logger) : BackgroundService
{
    public async Task<CatalogMatchingLook> LookAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        IServiceProvider services = scope.ServiceProvider;
        DateTime now = Now();
        SpareTimeVerdict verdict = SpareTime.Judge(await services.GetRequiredService<IOccupancyReader>().ReadAsync(now, cancellationToken), now);

        if (verdict is not SpareTimeVerdict.Spare || await services.GetRequiredService<ICatalogWorklist>().NextAsync(cancellationToken) is not { } next)
        {
            return new CatalogMatchingLook(verdict, null, false);
        }

        CatalogMatched matched = await services.GetRequiredService<CatalogMatching>().MatchAsync(next, settings.Around, now, cancellationToken);

        if (!await services.GetRequiredService<ILearningSwitch>().IsOnAsync(cancellationToken))
        {
            return new CatalogMatchingLook(SpareTimeVerdict.LearningOff, null, false);
        }

        await services.GetRequiredService<ICatalogRepository>().KeepAsync(matched.Change, cancellationToken);

        ExtractionChange marked = await records.ChangeAsync(next.RecordingId, record => Mark(record, next.UpdatedAt), cancellationToken);

        return new CatalogMatchingLook(verdict, matched, marked is ExtractionChange.Written);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan waiting = settings.BeforeFirstLook;

        while (await WaitAsync(waiting, stoppingToken))
        {
            CatalogMatchingLook? look = await LookOnceAsync(stoppingToken);

            waiting = look?.Matched is null ? settings.BetweenLooks : settings.AfterAMatch;
        }
    }

    private async Task<CatalogMatchingLook?> LookOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            CatalogMatchingLook look = await LookAsync(stoppingToken);

            Told(look);

            return look;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception failure)
        {
            logger.LogError(failure, "A look for a recording to match against the catalog failed; the next one is unaffected.");

            return null;
        }
    }

    private bool Mark(LearningExtraction record, DateTime matchedAsOf)
    {
        if (!record.AwaitsMatching || record.UpdatedAt != matchedAsOf)
        {
            return false;
        }

        record.Matched(Now());

        return true;
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private async Task<bool> WaitAsync(TimeSpan waiting, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(waiting, clock, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        return true;
    }

    private void Told(CatalogMatchingLook look)
    {
        if (look.Matched is not { } matched)
        {
            return;
        }

        logger.LogInformation(
            "Recording {Recording} was matched against the catalog: {Matches} match(es), {Started} entr(ies) started, "
            + "{Reestimated} estimated again and {Found} stretch(es) found; its record was {Marked}.",
            matched.Recording.Wire,
            matched.Matches,
            matched.Change.Started.Count,
            matched.Change.Reestimated.Count,
            matched.Change.Found.Count,
            look.Marked ? "marked as matched" : "left waiting, having changed meanwhile");
    }
}
