using Carina.Domain.Encodings;
using Carina.Domain.Segments;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Encodings;

public enum ChapterRewriteOutcome
{
    Rewritten = 1,

    AsCarried = 2,

    PassedOver = 3,

    Abandoned = 4,

    Failed = 5,
}

/// <summary>
/// What one round of comparing standing artefacts' chapters with the segments did: how many artefacts were
/// read, what became of each, the jobs whose artefact could not be written again, and whether the round gave
/// way to the machine being needed.
/// </summary>
public sealed record ChapterRewriteRound(
    int Read,
    int Rewritten,
    int AsCarried,
    int PassedOver,
    int Abandoned,
    IReadOnlyList<EncodeJobId> Failed,
    bool Yielded);

/// <summary>
/// Compares the chapters standing artefacts carry with the ones their recording's detection by the version in
/// use would embed now, one artefact at a time, and writes an artefact again with the new chapters when
/// <see cref="ChapterRewrite"/> says it is due.
/// </summary>
/// <remarks>
/// An artefact whose chapters still agree is written down as standing for the detection and left as it is.
/// One opened in the last <see cref="ArtefactCaptionTracks.LeftAloneAfterAnOpening"/>, one not where the ledger
/// says, one written again within a day, and one whose room holds less than the artefact's size are passed
/// over. The copy is made beside the artefact and checked, and only once the machine is still spare and the
/// artefact is seen to stand, to be the file it was and to be unopened, does the placer rename the copy over
/// it; otherwise the copy is abandoned and tried again later. Whatever is not renamed is swept with the job's
/// scratch.
/// </remarks>
public sealed class ArtefactChapterRewrites(
    IChapterRewriteWorklist worklist,
    ICaptionTrackWorklist standing,
    IEncodeJobRepository jobs,
    IEncodeChapterRepository chapters,
    ChapterRewriteMux mux,
    EncodePlaces places,
    EncodeArtefactPlacer placer,
    EncodeScratchCleaner cleaner,
    IArtefactOpenings openings,
    IFreeSpaceReader room,
    TimeProvider clock,
    ILogger<ArtefactChapterRewrites> logger)
{
    public async Task<ChapterRewriteRound> RewriteAsync(
        int modelVersion,
        int atMost,
        IReadOnlyCollection<EncodeJobId> passedOver,
        Func<CancellationToken, Task<bool>> spare,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(atMost, 1);
        ArgumentNullException.ThrowIfNull(passedOver);
        ArgumentNullException.ThrowIfNull(spare);

        IReadOnlyList<ChapterRewriteSubject> awaiting = await worklist.AwaitingAsync(
            modelVersion,
            Now() - ChapterRewrite.OncePer,
            passedOver,
            atMost,
            cancellationToken);
        Dictionary<ChapterRewriteOutcome, int> settled = [];
        List<EncodeJobId> failed = [];

        foreach (ChapterRewriteSubject subject in awaiting)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await spare(cancellationToken))
            {
                return Round(awaiting.Count, settled, failed, yielded: true);
            }

            ChapterRewriteOutcome outcome = await ComparedAsync(subject, spare, cancellationToken);
            settled[outcome] = settled.GetValueOrDefault(outcome) + 1;

            if (outcome is ChapterRewriteOutcome.Failed)
            {
                failed.Add(subject.Job.Id);
            }
        }

        return Round(awaiting.Count, settled, failed, yielded: false);
    }

    private static ChapterRewriteRound Round(int read, Dictionary<ChapterRewriteOutcome, int> settled, List<EncodeJobId> failed, bool yielded)
        => new(
            read,
            settled.GetValueOrDefault(ChapterRewriteOutcome.Rewritten),
            settled.GetValueOrDefault(ChapterRewriteOutcome.AsCarried),
            settled.GetValueOrDefault(ChapterRewriteOutcome.PassedOver),
            settled.GetValueOrDefault(ChapterRewriteOutcome.Abandoned),
            [.. failed],
            yielded);

    private async Task<ChapterRewriteOutcome> ComparedAsync(
        ChapterRewriteSubject subject,
        Func<CancellationToken, Task<bool>> spare,
        CancellationToken cancellationToken)
    {
        EncodeJob job = subject.Job;

        try
        {
            return await CompareAsync(subject, spare, cancellationToken);
        }
        catch (EncodeJobMovedMeanwhileException)
        {
            logger.LogInformation(
                "Job {Job} was moved in the ledger while its artefact's chapters were compared with the segments; the ledger's word stands.",
                job.Id.Wire);

            return ChapterRewriteOutcome.Abandoned;
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            logger.LogWarning(
                failure,
                "Writing the chapters of the artefact of job {Job} again threw; the artefact is left as it was.",
                job.Id.Wire);

            return ChapterRewriteOutcome.Failed;
        }
        finally
        {
            await cleaner.ClearAsync(job, CancellationToken.None);
        }
    }

    private async Task<ChapterRewriteOutcome> CompareAsync(
        ChapterRewriteSubject subject,
        Func<CancellationToken, Task<bool>> spare,
        CancellationToken cancellationToken)
    {
        EncodeJob job = subject.Job;

        if (job.Timeline is not { } timeline)
        {
            return ChapterRewriteOutcome.PassedOver;
        }

        EncodeChapterChoice choice = EncodeChapterChoice.Of(true, subject.Detection.ModelVersion, SegmentStanding.Of(null, subject.Detection));
        IReadOnlyList<ChapterSegment> wanted = choice.Chapters(PlaybackFileClock.Of(timeline));
        IReadOnlyList<ChapterSegment> carried = [.. (await chapters.ListForJobAsync(job.Id, cancellationToken)).Select(chapter => chapter.Segment)];

        switch (ChapterRewrite.Judge(carried, wanted, subject.LastRewrittenAt, Now()))
        {
            case ChapterRewriteVerdict.AsCarried:
                await StandsForAsync(job, choice, cancellationToken);

                return ChapterRewriteOutcome.AsCarried;
            case ChapterRewriteVerdict.RewrittenToday:
                return ChapterRewriteOutcome.PassedOver;
            default:
                return await RewrittenAsync(job, choice, wanted, spare, cancellationToken);
        }
    }

    private async Task<ChapterRewriteOutcome> RewrittenAsync(
        EncodeJob job,
        EncodeChapterChoice choice,
        IReadOnlyList<ChapterSegment> wanted,
        Func<CancellationToken, Task<bool>> spare,
        CancellationToken cancellationToken)
    {
        if (Where(job) is not { } artefact || !File.Exists(artefact))
        {
            logger.LogWarning(
                "The artefact of job {Job} is not where the ledger says under output root {Root}, so its chapters are not written again.",
                job.Id.Wire,
                job.OutputRoot.Value);

            return ChapterRewriteOutcome.PassedOver;
        }

        FileInfo before = new(artefact);
        (long Bytes, DateTime WrittenAt) was = (before.Length, before.LastWriteTimeUtc);

        if (OpenedLately(job) || !RoomFor(job, was.Bytes))
        {
            return ChapterRewriteOutcome.PassedOver;
        }

        ChapterRewriteNames names = ChapterRewriteNames.Tried(job);
        ChapterRewriteMade made = await mux.MakeAsync(job, artefact, wanted, names, cancellationToken);

        if (!made.Made)
        {
            logger.LogWarning("The chapters of the artefact of job {Job} could not be written again: {Note}", job.Id.Wire, made.Note);

            return ChapterRewriteOutcome.Failed;
        }

        if (!await spare(cancellationToken) || !await StillAsItWasAsync(job, artefact, was, cancellationToken))
        {
            logger.LogInformation(
                "The artefact of job {Job} was needed, changed, went or was opened while its chapters were written again; the copy is dropped and tried again later.",
                job.Id.Wire);

            return ChapterRewriteOutcome.Abandoned;
        }

        RenameVerdict put = await placer.PutChapteredInPlaceAsync(job, names.Chaptered, made.Rewritten!, cancellationToken);

        if (!put.IsARename)
        {
            logger.LogWarning("The artefact of job {Job} could not be replaced by the copy with its chapters written again: {Note}", job.Id.Wire, put.Note);

            return ChapterRewriteOutcome.Failed;
        }

        await chapters.RecordAsync(job.Id, EncodeChapter.Mark(job.Id, wanted), cancellationToken);
        await StandsForAsync(job, choice, cancellationToken);

        logger.LogInformation(
            "The artefact of job {Job} was written again with the {Chapters} chapter(s) of the detection by version {Version}.",
            job.Id.Wire,
            wanted.Count,
            choice.ModelVersion);

        return ChapterRewriteOutcome.Rewritten;
    }

    private async Task StandsForAsync(EncodeJob job, EncodeChapterChoice choice, CancellationToken cancellationToken)
    {
        if (await jobs.FindAsync(job.Id, cancellationToken) is not { StandsAsTheArtefact: true } held)
        {
            logger.LogInformation(
                "The artefact of job {Job} stopped standing while its chapters were compared with the segments, so what they stand for is not written down.",
                job.Id.Wire);

            return;
        }

        held.Rechaptered(choice.Reading(Now()));
        await jobs.SaveAsync(held, cancellationToken);
    }

    private bool RoomFor(EncodeJob job, long bytes)
    {
        if (places.WhereTheWorkGoes(job.OutputRoot) is { } workshop && room.FreeBytes(workshop) is { } free && free >= bytes)
        {
            return true;
        }

        logger.LogInformation(
            "The chapters of the artefact of job {Job} are not written again yet, because the room its copy would be written in does not hold the artefact's {Bytes} bytes.",
            job.Id.Wire,
            bytes);

        return false;
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
               && await standing.StandsWithNothingInHandAsync(job, cancellationToken);
    }

    private bool OpenedLately(EncodeJob job)
        => openings.LastOpened(job.OutputRoot, job.ArtefactName!.Value) is { } opened
           && clock.GetUtcNow() - opened < ArtefactCaptionTracks.LeftAloneAfterAnOpening;

    private string? Where(EncodeJob job)
        => job.ArtefactName is { } name && places.WhereTheArtefactGoes(job.OutputRoot) is { } shelf ? Path.Combine(shelf, name.Value) : null;

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
}
