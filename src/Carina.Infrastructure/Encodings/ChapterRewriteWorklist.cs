using Carina.Domain.Encodings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Encodings;

public sealed class ChapterRewriteWorklist(CarinaDbContext context) : IChapterRewriteWorklist
{
    public async Task<IReadOnlyList<ChapterRewriteSubject>> AwaitingAsync(
        int modelVersion,
        DateTime rewrittenSince,
        IReadOnlyCollection<EncodeJobId> passedOver,
        int atMost,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(passedOver);
        ArgumentOutOfRangeException.ThrowIfLessThan(atMost, 1);

        EncodeJobId[] skipped = [.. passedOver];

        List<Row> rows = await (
                from job in Standing()
                join detection in Detections(modelVersion) on job.RecordingId equals detection.RecordingId into detected
                from detection in detected.DefaultIfEmpty()
                join answer in context.Set<RecordingAnswer>().AsNoTracking() on job.RecordingId equals answer.RecordingId into answered
                from answer in answered.DefaultIfEmpty()
                let chapters = job.Chapters
                where job.Timeline != null
                      && (answer != null && answer.WithdrawnAt == null
                          ? chapters == null || chapters.AnsweredAt == null || chapters.AnsweredAt != answer.AnsweredAt
                          : (chapters != null && chapters.AnsweredAt != null)
                            || (detection != null
                                && (chapters == null
                                    || chapters.ModelVersion != modelVersion
                                    || chapters.DecidedAt < detection.DetectedAt)))
                      && !skipped.Contains(job.Id)
                      && !InHand().Any(other => other.RecordingId == job.RecordingId)
                      && !Rewritten().Any(scratch => scratch.JobId == job.Id && scratch.RemovedAt >= rewrittenSince)
                orderby (answer != null ? answer.WithdrawnAt ?? answer.AnsweredAt : detection!.DetectedAt) descending, job.Id
                select new Row(job, detection, answer, Rewritten().Where(scratch => scratch.JobId == job.Id).Max(scratch => scratch.RemovedAt)))
            .Take(atMost)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new ChapterRewriteSubject(row.Job, row.Detection, row.Answer, row.LastRewrittenAt))];
    }

    private IQueryable<RecordingDetection> Detections(int modelVersion)
        => context.Set<RecordingDetection>().AsNoTracking().Where(detection => detection.ModelVersion == modelVersion);

    private IQueryable<EncodeJob> Standing()
        => context.Set<EncodeJob>()
            .AsNoTracking()
            .Where(job => job.Status == EncodeJobStatus.Completed && job.ArtefactName != null && job.ReplacedAt == null);

    private IQueryable<EncodeJob> InHand()
        => context.Set<EncodeJob>()
            .AsNoTracking()
            .Where(job => job.Status == EncodeJobStatus.Queued || job.Status == EncodeJobStatus.Running);

    private IQueryable<EncodeScratchFile> Rewritten()
        => context.Set<EncodeScratchFile>()
            .AsNoTracking()
            .Where(scratch => scratch.Kind == EncodeScratchKind.ChapteredWork && scratch.Fate == EncodeScratchFate.BecameTheArtefact);

    private sealed record Row(EncodeJob Job, RecordingDetection? Detection, RecordingAnswer? Answer, DateTime? LastRewrittenAt);
}
