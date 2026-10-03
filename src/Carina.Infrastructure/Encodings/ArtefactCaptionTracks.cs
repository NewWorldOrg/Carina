using Carina.Domain.Captions;
using Carina.Domain.Encodings;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// Puts a text track of captions into artefacts already made, one at a time, newest recording first.
/// </summary>
/// <remarks>
/// An artefact somebody opened in the last <see cref="LeftAloneAfterAnOpening"/> is passed over, and so is a
/// recording a job of which is waiting or running. The copy with the track put in is made beside the
/// artefact and checked, and only then, once the artefact is seen to stand, to be the file it was and to be
/// unopened, does the placer rename the copy over it. Whatever is not renamed is swept with the job's scratch.
/// </remarks>
public sealed class ArtefactCaptionTracks(
    ICaptionTrackWorklist worklist,
    IEncodeJobRepository jobs,
    ICaptionRecords records,
    CaptionTrackMux mux,
    EncodePlaces places,
    EncodeArtefactPlacer placer,
    EncodeScratchCleaner cleaner,
    IArtefactOpenings openings,
    TimeProvider clock,
    ILogger<ArtefactCaptionTracks> logger) : IArtefactCaptioning
{
    public static readonly TimeSpan LeftAloneAfterAnOpening = TimeSpan.FromMinutes(30);

    public async Task<ArtefactCaptioningRound> CaptionAsync(
        int atMost,
        Func<CancellationToken, Task<bool>> busy,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(atMost, 1);
        ArgumentNullException.ThrowIfNull(busy);

        IReadOnlyList<CaptionTrackSubject> awaiting = await worklist.AwaitingAsync(atMost, cancellationToken);
        Dictionary<EncodeCaptionTrack, int> settled = [];

        foreach (CaptionTrackSubject subject in awaiting)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await busy(cancellationToken))
            {
                return Round(awaiting.Count, settled, yielded: true);
            }

            if (await TrackedAsync(subject, cancellationToken) is { } outcome)
            {
                settled[outcome] = settled.GetValueOrDefault(outcome) + 1;
            }
        }

        return Round(awaiting.Count, settled, yielded: false);
    }

    private static ArtefactCaptioningRound Round(int read, Dictionary<EncodeCaptionTrack, int> settled, bool yielded)
        => new(
            read,
            settled.GetValueOrDefault(EncodeCaptionTrack.Added),
            settled.GetValueOrDefault(EncodeCaptionTrack.Withheld),
            settled.GetValueOrDefault(EncodeCaptionTrack.Failed),
            yielded);

    private async Task<EncodeCaptionTrack?> TrackedAsync(CaptionTrackSubject subject, CancellationToken cancellationToken)
    {
        EncodeJob job = subject.Job;

        try
        {
            return await TrackAsync(job, subject.CaptionsMadeAt, cancellationToken);
        }
        catch (EncodeJobMovedMeanwhileException)
        {
            logger.LogInformation(
                "Job {Job} was moved in the ledger while the text track of captions was put into its artefact; the ledger's word stands.",
                job.Id.Wire);

            return null;
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            logger.LogWarning(
                failure,
                "Putting the text track of captions into the artefact of job {Job} threw; the artefact is left as it was.",
                job.Id.Wire);

            return await SettleAsync(job, EncodeCaptionTrack.Failed, subject.CaptionsMadeAt, cancellationToken);
        }
        finally
        {
            await cleaner.ClearAsync(job, CancellationToken.None);
        }
    }

    private async Task<EncodeCaptionTrack?> TrackAsync(EncodeJob job, DateTime madeAt, CancellationToken cancellationToken)
    {
        if (Where(job) is not { } artefact || !File.Exists(artefact.Path))
        {
            logger.LogWarning(
                "The artefact of job {Job} is not where the ledger says under output root {Root}, so no text track is put into it.",
                job.Id.Wire,
                job.OutputRoot.Value);

            return null;
        }

        if (OpenedLately(job) || await records.ReadAsync(job.RecordingId, cancellationToken) is not { Lines: not null } record)
        {
            return null;
        }

        FileInfo before = new(artefact.Path);
        (long Bytes, DateTime WrittenAt) was = (before.Length, before.LastWriteTimeUtc);
        CaptionTrackMade made = await mux.MakeAsync(job, artefact.Path, record, cancellationToken);

        if (made.Outcome is not EncodeCaptionTrack.Added)
        {
            return await SettleAsync(job, made.Outcome, madeAt, cancellationToken, made.Note);
        }

        if (!await StillAsItWasAsync(job, artefact.Path, was, cancellationToken))
        {
            logger.LogInformation(
                "The artefact of job {Job} changed, went or was opened while its text track was being made; the copy is dropped and tried again later.",
                job.Id.Wire);

            return null;
        }

        RenameVerdict put = placer.PutCaptionedInPlace(job, made.Captioned!);

        return put.IsARename
            ? await SettleAsync(job, EncodeCaptionTrack.Added, madeAt, cancellationToken)
            : await SettleAsync(job, EncodeCaptionTrack.Failed, madeAt, cancellationToken, put.Note);
    }

    private async Task<bool> StillAsItWasAsync(
        EncodeJob job,
        string artefact,
        (long Bytes, DateTime WrittenAt) was,
        CancellationToken cancellationToken)
    {
        FileInfo now = new(artefact);

        return now.Exists
               && (now.Length, now.LastWriteTimeUtc) == was
               && !OpenedLately(job)
               && await worklist.StandsWithNothingInHandAsync(job, cancellationToken);
    }

    private bool OpenedLately(EncodeJob job)
        => openings.LastOpened(job.OutputRoot, job.ArtefactName!.Value) is { } opened
           && clock.GetUtcNow() - opened < LeftAloneAfterAnOpening;

    private async Task<EncodeCaptionTrack?> SettleAsync(
        EncodeJob job,
        EncodeCaptionTrack outcome,
        DateTime madeAt,
        CancellationToken cancellationToken,
        string note = "")
    {
        job.Tracked(outcome, madeAt);
        await jobs.SaveAsync(job, cancellationToken);

        logger.Log(
            outcome is EncodeCaptionTrack.Failed ? LogLevel.Warning : LogLevel.Information,
            "The text track of captions for the artefact of job {Job} came to {Outcome}. {Note}",
            job.Id.Wire,
            outcome,
            note);

        return outcome;
    }

    private ArtefactPlace? Where(EncodeJob job)
    {
        if (job.ArtefactName is not { } name || places.WhereTheArtefactGoes(job.OutputRoot) is not { } room)
        {
            return null;
        }

        return new ArtefactPlace(Path.Combine(room, name.Value));
    }

    private sealed record ArtefactPlace(string Path);
}
