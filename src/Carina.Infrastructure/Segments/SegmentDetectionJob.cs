using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Segments;

/// <summary>
/// What one detection did: what in spare time stood in the way when nothing was detected, null when nothing was
/// asked of spare time; and the detection kept.
/// </summary>
public sealed record SegmentDetectionLook(SpareTimeVerdict? Verdict, RecordingDetection? Detected);

/// <summary>
/// Detects the segments of recordings with the version of the model in use, one at a time, on the two paths
/// matching against the catalog takes, and after it. A recording followed to its end and matched at once
/// (<see cref="MatchedAtOnce"/>) is detected at once, whatever is being recorded or watched. Every other
/// recording that <see cref="RecordingDetection.IsAwaited"/> and is still kept — one that had data before,
/// one matched again, one whose catalog changed under it (<see cref="LearningExtraction.CatalogChanged"/>), and every
/// one when the version in use changes — waits for <see cref="SpareTime"/> and is
/// detected the most recently started first, one a look: after
/// <see cref="SegmentDetectionSettings.BeforeFirstLook"/>, then every
/// <see cref="SegmentDetectionSettings.BetweenLooks"/>, or after
/// <see cref="SegmentDetectionSettings.AfterADetection"/> once a look has detected one. A detection takes the
/// place of the one kept for the same recording and version; none is removed. A recording whose
/// detection fails is passed over until the process starts again, so that it does not hold back the rest.
/// </summary>
public sealed class SegmentDetectionJob(
    IServiceScopeFactory scopes,
    LearningRecords records,
    MatchedAtOnce matched,
    SegmentDetectionSettings settings,
    TimeProvider clock,
    ILogger<SegmentDetectionJob> logger) : BackgroundService
{
    private readonly Lock gate = new();

    private readonly HashSet<RecordingId> passedOver = [];

    public IReadOnlyCollection<RecordingId> PassedOver
    {
        get
        {
            lock (gate)
            {
                return [.. passedOver];
            }
        }
    }

    /// <summary>
    /// The first path: detects at once the recording matched at once, when it waits to be detected by the version
    /// in use.
    /// </summary>
    public async Task<SegmentDetectionLook> DetectMatchedAsync(RecordingId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        IServiceProvider services = scope.ServiceProvider;

        if (await records.FindAsync(id, cancellationToken) is not { } record)
        {
            return new SegmentDetectionLook(null, null);
        }

        SegmentModelVersion version = await services.GetRequiredService<ISegmentModelVersionRepository>().ActiveAsync(cancellationToken);
        RecordingDetection? kept = await services.GetRequiredService<IRecordingDetectionRepository>().FindAsync(id, version.Number, cancellationToken);

        return RecordingDetection.IsAwaited(record, kept)
            ? await DetectAsync(services, record, version, null, cancellationToken)
            : new SegmentDetectionLook(null, null);
    }

    /// <summary>
    /// The second path: in spare time, detects the most recently started recording still kept that waits to be
    /// detected by the version in use.
    /// </summary>
    public async Task<SegmentDetectionLook> LookAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        IServiceProvider services = scope.ServiceProvider;
        DateTime now = Now();
        SpareTimeVerdict verdict = SpareTime.Judge(await services.GetRequiredService<IOccupancyReader>().ReadAsync(now, cancellationToken), now);

        if (verdict is not SpareTimeVerdict.Spare)
        {
            return new SegmentDetectionLook(verdict, null);
        }

        SegmentModelVersion version = await services.GetRequiredService<ISegmentModelVersionRepository>().ActiveAsync(cancellationToken);

        return await services.GetRequiredService<IDetectionWorklist>().NextAsync(version.Number, PassedOver, cancellationToken) is { } next
            ? await DetectAsync(services, next, version, verdict, cancellationToken)
            : new SegmentDetectionLook(verdict, null);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan waiting = settings.BeforeFirstLook;

        while (await WaitAsync(waiting, stoppingToken))
        {
            if (matched.TryTake(out RecordingId? id))
            {
                await OnceAsync(token => DetectMatchedAsync(id, token), stoppingToken);

                continue;
            }

            SegmentDetectionLook? look = await OnceAsync(LookAsync, stoppingToken);

            waiting = look?.Detected is null ? settings.BetweenLooks : settings.AfterADetection;
        }
    }

    private async Task<SegmentDetectionLook> DetectAsync(
        IServiceProvider services,
        LearningExtraction record,
        SegmentModelVersion version,
        SpareTimeVerdict? verdict,
        CancellationToken cancellationToken)
    {
        RecordingDetection detection;

        try
        {
            detection = await services.GetRequiredService<SegmentDetecting>().DetectAsync(record, version, Now(), cancellationToken);
        }
        catch (Exception failure) when (!cancellationToken.IsCancellationRequested)
        {
            PassOver(record.RecordingId, failure);

            return new SegmentDetectionLook(verdict, null);
        }

        await services.GetRequiredService<IRecordingDetectionRepository>().KeepAsync(detection, cancellationToken);

        return new SegmentDetectionLook(verdict, detection);
    }

    private void PassOver(RecordingId id, Exception failure)
    {
        lock (gate)
        {
            passedOver.Add(id);
        }

        logger.LogError(failure, "Recording {Recording} could not be detected; it is passed over until the app starts again.", id.Wire);
    }

    private async Task<SegmentDetectionLook?> OnceAsync(Func<CancellationToken, Task<SegmentDetectionLook>> detecting, CancellationToken stoppingToken)
    {
        try
        {
            SegmentDetectionLook look = await detecting(stoppingToken);

            Told(look);

            return look;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception failure)
        {
            logger.LogError(failure, "A detection of segments failed; the next one is unaffected.");

            return null;
        }
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
        Task told = matched.WaitAsync(either.Token);

        await Task.WhenAny(elapsed, told);
        await either.CancelAsync();
        await Task.WhenAll(elapsed, told).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        return !stoppingToken.IsCancellationRequested;
    }

    private void Told(SegmentDetectionLook look)
    {
        if (look.Detected is not { } detected)
        {
            return;
        }

        logger.LogInformation(
            "Recording {Recording} was detected by version {Version} {Path}: {State}, {Stretches} stretch(es), breaks taking {Share:P1}.",
            detected.RecordingId.Wire,
            detected.ModelVersion,
            look.Verdict is null ? "as soon as it was matched" : "in spare time",
            detected.State,
            detected.Segments.Count,
            detected.CmShare);
    }
}
