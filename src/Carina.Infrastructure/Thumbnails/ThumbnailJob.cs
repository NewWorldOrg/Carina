using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Domain.Thumbnails;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Thumbnails;

public sealed class ThumbnailJob(
    IServiceScopeFactory scopes,
    IThumbnailRenderer renderer,
    ThumbnailSettings settings,
    IntegritySettings mounts,
    TimeProvider clock,
    ILogger<ThumbnailJob> logger) : BackgroundService
{
    public const string Extension = ".jpg";

    private int running;

    public async Task<ThumbnailPass> RunAsync(CancellationToken cancellationToken)
    {
        if (!settings.DrawsAnything)
        {
            return ThumbnailPass.RefusedBecauseThereIsNowhereToPutThem();
        }

        if (Interlocked.CompareExchange(ref running, 1, 0) is not 0)
        {
            return ThumbnailPass.RefusedBecauseOneIsRunning();
        }

        try
        {
            return await PassAsync(cancellationToken);
        }
        finally
        {
            Interlocked.Exchange(ref running, 0);
        }
    }

    public async Task<bool> RemakeAsync(RecordingId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (!settings.DrawsAnything)
        {
            return false;
        }

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        IThumbnailWorklist worklist = scope.ServiceProvider.GetRequiredService<IThumbnailWorklist>();
        ThumbnailSubject? subject = await worklist.AskAgainAsync(id, cancellationToken);

        if (subject is null)
        {
            return false;
        }

        await WorkOnAsync(worklist, subject, cancellationToken);

        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.DrawsAnything)
        {
            logger.LogWarning(
                "No directory is configured for thumbnails, so no recording is ever illustrated.");

            return;
        }

        TimeSpan waiting = settings.BeforeFirstPass;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(waiting, clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            waiting = settings.BetweenPasses;

            try
            {
                await RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception failure)
            {
                logger.LogError(failure, "A thumbnail pass failed; the next one is unaffected.");
            }
        }
    }

    private async Task<ThumbnailPass> PassAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        IThumbnailWorklist worklist = scope.ServiceProvider.GetRequiredService<IThumbnailWorklist>();
        IReadOnlyList<ThumbnailSubject> awaiting =
            await worklist.AwaitingAsync(settings.AtMostAPass, cancellationToken);

        int drawn = 0;
        int skipped = 0;
        int failed = 0;

        foreach (ThumbnailSubject subject in awaiting)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ThumbnailState? settled = await WorkOnAsync(worklist, subject, cancellationToken);

            switch (settled)
            {
                case ThumbnailState.Ready:
                    drawn++;
                    break;
                case ThumbnailState.Skipped:
                    skipped++;
                    break;
                case ThumbnailState.Failed:
                    failed++;
                    break;
                default:
                    break;
            }
        }

        logger.LogInformation(
            "A thumbnail pass read {Read} recording(s): {Drawn} drawn, {Skipped} skipped, {Failed} failed.",
            awaiting.Count,
            drawn,
            skipped,
            failed);

        return ThumbnailPass.Of(awaiting.Count, drawn, skipped, failed);
    }

    private async Task<ThumbnailState?> WorkOnAsync(
        IThumbnailWorklist worklist,
        ThumbnailSubject subject,
        CancellationToken cancellationToken)
    {
        try
        {
            ThumbnailPlan plan = ThumbnailPlan.For(subject, settings);

            if (plan.Intent is ThumbnailIntent.Skip)
            {
                await worklist.IllustrateAsync(subject.Id, ThumbnailState.Skipped, null, cancellationToken);

                return ThumbnailState.Skipped;
            }

            if (Mounted(subject.Root) is not { } source)
            {
                logger.LogWarning(
                    "Output root {Root} is named by the ledger and nothing tells this process where it is mounted, "
                    + "so recording {Recording} keeps its place in the queue.",
                    subject.Root.Value,
                    subject.Id.Wire);

                return null;
            }

            ThumbnailRender render = await renderer.RenderAsync(
                new ThumbnailRequest(
                    Path.Combine(source, subject.FileName.Value),
                    Path.Combine(settings.WrittenTo!, subject.Id.Wire + Extension),
                    plan.At),
                cancellationToken);

            if (render.Fault is { } fault)
            {
                logger.LogWarning(
                    "Recording {Recording} has no picture: {Fault} ({ExitCode}). {Note}",
                    subject.Id.Wire,
                    fault,
                    render.ExitCode,
                    render.Note);

                await worklist.IllustrateAsync(subject.Id, ThumbnailState.Failed, fault, cancellationToken);

                return ThumbnailState.Failed;
            }

            await worklist.IllustrateAsync(subject.Id, ThumbnailState.Ready, null, cancellationToken);

            if (plan.OfSomethingUnfinished)
            {
                logger.LogInformation(
                    "Recording {Recording} is illustrated, and the ledger says the recording is unfinished.",
                    subject.Id.Wire);
            }

            return ThumbnailState.Ready;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception failure)
        {
            logger.LogError(
                failure,
                "Illustrating recording {Recording} threw, which leaves the recording itself untouched.",
                subject.Id.Wire);

            return null;
        }
    }

    private string? Mounted(OutputRoot root)
        => mounts.OutputRoots.FirstOrDefault(candidate => candidate.Root.Equals(root))?.Path;
}
