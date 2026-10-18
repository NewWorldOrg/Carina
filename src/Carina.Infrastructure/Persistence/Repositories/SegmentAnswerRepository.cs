using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class SegmentAnswerRepository(CarinaDbContext context) : ISegmentAnswerRepository
{
    public async Task<RecordingAnswer?> FindAsync(RecordingId recording, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recording);

        return await context.Set<RecordingAnswer>()
            .AsNoTracking()
            .FirstOrDefaultAsync(answer => answer.RecordingId == recording, cancellationToken);
    }

    /// <summary>
    /// Inserts the answer as the first one sent, or writes it over the one kept, one more answer counted and
    /// the withdrawal cleared, in a single statement.
    /// </summary>
    public async Task AnswerAsync(RecordingAnswer answer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(answer);

        Guid recording = answer.RecordingId.Value;
        string segments = SegmentJson.Written(answer.Segments);
        TimeSpan length = answer.Length;
        bool confirmed = answer.Confirmed;
        DateTime? confirmedAt = answer.ConfirmedAt;
        DateTime answeredAt = answer.AnsweredAt;

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO segment_answer (recording_id, segments, length, confirmed, confirmed_at, answered_at, answers, withdrawn_at)
            VALUES ({recording}, {segments}::jsonb, {length}::interval, {confirmed}, {confirmedAt}::timestamptz, {answeredAt}, 1, NULL)
            ON CONFLICT (recording_id)
            DO UPDATE SET
                segments = EXCLUDED.segments,
                length = EXCLUDED.length,
                confirmed = EXCLUDED.confirmed,
                confirmed_at = EXCLUDED.confirmed_at,
                answered_at = EXCLUDED.answered_at,
                answers = segment_answer.answers + 1,
                withdrawn_at = NULL
            """,
            cancellationToken);
    }

    public async Task<bool> WithdrawAsync(RecordingId recording, DateTime at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recording);

        Guid id = recording.Value;

        return await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE segment_answer SET withdrawn_at = {at} WHERE recording_id = {id} AND withdrawn_at IS NULL",
            cancellationToken) is 1;
    }

    public async Task<EvaluationEvidence> EvidenceAsync(CancellationToken cancellationToken)
        => EvaluationEvidence.OfAnswers(
            await context.Set<RecordingAnswer>()
                .AsNoTracking()
                .Where(answer => answer.Confirmed && answer.WithdrawnAt == null)
                .ToListAsync(cancellationToken));
}
