using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Segments;

/// <summary>
/// What one look at the catalog did: what stood in the way when nothing was done — spare time, or learning
/// being off; null when nothing was asked of spare time — what the matching found and changed, whether the
/// record was marked as matched, and the entry estimated again when no recording waited to be matched.
/// </summary>
public sealed record CatalogMatchingLook(SpareTimeVerdict? Verdict, CatalogMatched? Matched, bool Marked, CatalogEntry? Reestimated = null);

/// <summary>
/// Matches recordings against the catalog, one at a time, on two paths. A recording whose follow came to its
/// end as it was recorded (<see cref="FollowedToTheEnd"/>) and whose data was read to the end is matched at
/// once, whatever is being recorded or watched, so that it is matched before its detection and encoding are
/// due. Every other recording whose data waits to be matched — those that had data before the process
/// started, those read afterwards and those read again — waits for <see cref="SpareTime"/> and is matched the
/// most recently started first, one a look: after <see cref="CatalogMatchingSettings.BeforeFirstLook"/>, then
/// every <see cref="CatalogMatchingSettings.BetweenLooks"/>, or after
/// <see cref="CatalogMatchingSettings.AfterAMatch"/> once a look has matched one. A recording told to the
/// first path is taken as soon as it is told, before the next look, and once marked as matched it is told on
/// (<see cref="MatchedAtOnce"/>) to be detected at once. Neither path matches while learning is off, and a
/// match finished after learning was switched off is not written. What a match changes is written
/// before the record is marked as matched; a record that changed meanwhile is left waiting and is matched
/// again, which adds no stretch twice. A look in spare time that finds no recording waiting estimates again
/// the entry first seen earliest of those last estimated with an earlier version of the estimation
/// (<see cref="CatalogEntry.AwaitsReestimation"/>), one a look until none is left, and is followed as soon as a
/// match is; an estimate finished after learning was switched off is not written either. Once a match or an estimate is
/// written, every recording that hears an entry whose kind or confidence it changed, or a stretch it took for another
/// kind, is taken as matched again (<see cref="LearningExtraction.CatalogChanged"/>), so that it waits to be detected
/// again; an estimate that leaves an entry as it was has none detected again.
/// </summary>
public sealed class CatalogMatchingJob(
    IServiceScopeFactory scopes,
    LearningRecords records,
    FollowedToTheEnd followed,
    MatchedAtOnce matchedAtOnce,
    CatalogMatchingSettings settings,
    TimeProvider clock,
    ILogger<CatalogMatchingJob> logger) : BackgroundService
{
    /// <summary>
    /// The first path: matches at once the recording followed to its end, when learning is on and its data
    /// was read to the end and waits to be matched.
    /// </summary>
    public async Task<CatalogMatchingLook> MatchFollowedAsync(RecordingId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        IServiceProvider services = scope.ServiceProvider;

        if (!await services.GetRequiredService<ILearningSwitch>().IsOnAsync(cancellationToken))
        {
            return new CatalogMatchingLook(SpareTimeVerdict.LearningOff, null, false);
        }

        if (await records.FindAsync(id, cancellationToken) is not { State: LearningExtractionState.Done, AwaitsMatching: true } record)
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

    /// <summary>
    /// The second path: in spare time, matches the most recently started recording whose data waits to be
    /// matched, or, when none waits, estimates again the next entry that waits for it.
    /// </summary>
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

        if (!await services.GetRequiredService<ILearningSwitch>().IsOnAsync(cancellationToken))
        {
            return new CatalogMatchingLook(SpareTimeVerdict.LearningOff, null, false);
        }

        ICatalogRepository catalog = services.GetRequiredService<ICatalogRepository>();

        await catalog.KeepAsync(matched.Change, cancellationToken);

        ExtractionChange marked = await records.ChangeAsync(record.RecordingId, found => Mark(found, record.UpdatedAt), cancellationToken);

        await HeardAnewAsync(catalog, matched.Change, cancellationToken);

        return new CatalogMatchingLook(verdict, matched, marked is ExtractionChange.Written);
    }

    private async Task<CatalogMatchingLook> ReestimateAsync(
        IServiceProvider services,
        CatalogEntry entry,
        SpareTimeVerdict verdict,
        CancellationToken cancellationToken)
    {
        CatalogChange change = await services.GetRequiredService<CatalogMatching>().ReestimateAsync(entry, Now(), cancellationToken);

        if (!await services.GetRequiredService<ILearningSwitch>().IsOnAsync(cancellationToken))
        {
            return new CatalogMatchingLook(SpareTimeVerdict.LearningOff, null, false);
        }

        ICatalogRepository catalog = services.GetRequiredService<ICatalogRepository>();

        await catalog.KeepAsync(change, cancellationToken);
        await HeardAnewAsync(catalog, change, cancellationToken);

        return new CatalogMatchingLook(verdict, null, false, entry);
    }

    /// <summary>
    /// Takes every recording that hears an entry <paramref name="change"/> recast, or a stretch it took for another
    /// kind, as matched again now, so that it waits to be detected again.
    /// </summary>
    private async Task HeardAnewAsync(ICatalogRepository catalog, CatalogChange change, CancellationToken cancellationToken)
    {
        IReadOnlyList<CatalogOccurrence> heard = change.Recast.Count is 0 ? [] : await catalog.OccurrencesOfAsync(change.Recast, cancellationToken);
        DateTime now = Now();

        foreach (RecordingId recording in heard.Concat(change.Overridden).Select(occurrence => occurrence.RecordingId).Distinct())
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

    /// <summary>
    /// Waits for <paramref name="waiting"/>, or until a recording is told to the first path; false once the
    /// process is stopping.
    /// </summary>
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
