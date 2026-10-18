using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// Where the answers are kept, one per recording. Nothing is removed from it: withdrawing an answer marks it,
/// and a recording thrown away leaves its answer where it is.
/// </summary>
public interface ISegmentAnswerRepository
{
    /// <summary>
    /// The answer kept for the recording, whether it stands or was withdrawn.
    /// </summary>
    Task<RecordingAnswer?> FindAsync(RecordingId recording, CancellationToken cancellationToken);

    /// <summary>
    /// Keeps <paramref name="answer"/> as the answer of its recording: the first one sent, or one written over the
    /// answer kept, counting one answer more than it did and standing again.
    /// </summary>
    Task AnswerAsync(RecordingAnswer answer, CancellationToken cancellationToken);

    /// <summary>
    /// Marks the answer of the recording withdrawn at <paramref name="at"/> when it stands, and says whether it did.
    /// </summary>
    Task<bool> WithdrawAsync(RecordingId recording, DateTime at, CancellationToken cancellationToken);
}
