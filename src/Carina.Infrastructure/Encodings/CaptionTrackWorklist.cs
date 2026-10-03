using Carina.Domain.Captions;
using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Encodings;

public sealed class CaptionTrackWorklist(CarinaDbContext context) : ICaptionTrackWorklist
{
    public async Task<IReadOnlyList<CaptionTrackSubject>> AwaitingAsync(int atMost, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(atMost, 1);

        List<Row> rows = await (
                from job in Standing()
                join recording in context.Set<Recording>().AsNoTracking() on job.RecordingId equals recording.Id
                where recording.CaptionState == CaptionState.Ready
                      && recording.CaptionsMadeAt != null
                      && (job.CaptionTrackFrom == null
                          || job.CaptionTrackFrom != recording.CaptionsMadeAt
                          || (job.CaptionTrack == EncodeCaptionTrack.Failed
                              && job.CaptionTrackAttempts < EncodeJob.CaptionTrackTriesAtMost))
                      && !InHand().Any(other => other.RecordingId == job.RecordingId)
                orderby recording.StoppedAtActual descending, recording.Id
                select new Row(job, recording.CaptionsMadeAt!.Value))
            .Take(atMost)
            .ToListAsync(cancellationToken);

        return [.. rows
            .Where(row => row.Job.AwaitsCaptionTrack(row.MadeAt))
            .Select(row => new CaptionTrackSubject(row.Job, row.MadeAt))];
    }

    public async Task<bool> StandsWithNothingInHandAsync(EncodeJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        return await Standing().AnyAsync(held => held.Id == job.Id, cancellationToken)
               && !await InHand().AnyAsync(other => other.RecordingId == job.RecordingId, cancellationToken);
    }

    private IQueryable<EncodeJob> Standing()
        => context.Set<EncodeJob>()
            .AsNoTracking()
            .Where(job => job.Status == EncodeJobStatus.Completed && job.ArtefactName != null && job.ReplacedAt == null);

    private IQueryable<EncodeJob> InHand()
        => context.Set<EncodeJob>()
            .AsNoTracking()
            .Where(job => job.Status == EncodeJobStatus.Queued || job.Status == EncodeJobStatus.Running);

    private sealed record Row(EncodeJob Job, DateTime MadeAt);
}
