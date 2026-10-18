using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.TestSupport;

/// <summary>
/// The answers held in memory, one per recording, as the store keeps them: an answer sent again takes the place
/// of the one held, one more answer counted and standing again, and withdrawing one marks it and keeps it.
/// </summary>
public sealed class HeldSegmentAnswers : ISegmentAnswerRepository
{
    private readonly Lock gate = new();

    private readonly Dictionary<RecordingId, RecordingAnswer> held = [];

    public int Writes { get; private set; }

    public RecordingAnswer? Of(RecordingId recording)
    {
        lock (gate)
        {
            return held.GetValueOrDefault(recording);
        }
    }

    public Task<RecordingAnswer?> FindAsync(RecordingId recording, CancellationToken cancellationToken)
        => Task.FromResult(Of(recording));

    public Task AnswerAsync(RecordingAnswer answer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(answer);

        lock (gate)
        {
            int answers = held.TryGetValue(answer.RecordingId, out RecordingAnswer? kept) ? kept.Answers + 1 : 1;

            held[answer.RecordingId] = RecordingAnswer.Rehydrate(
                answer.RecordingId,
                answer.Segments,
                answer.Length,
                answer.Confirmed,
                answer.ConfirmedAt,
                answer.AnsweredAt,
                answers,
                null);
            Writes++;
        }

        return Task.CompletedTask;
    }

    public Task<bool> WithdrawAsync(RecordingId recording, DateTime at, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (!held.TryGetValue(recording, out RecordingAnswer? kept) || !kept.Stands)
            {
                return Task.FromResult(false);
            }

            held[recording] = RecordingAnswer.Rehydrate(recording, kept.Segments, kept.Length, kept.Confirmed, kept.ConfirmedAt, kept.AnsweredAt, kept.Answers, at);
            Writes++;

            return Task.FromResult(true);
        }
    }

    public Task<EvaluationEvidence> EvidenceAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(EvaluationEvidence.OfAnswers([.. held.Values]));
        }
    }
}
