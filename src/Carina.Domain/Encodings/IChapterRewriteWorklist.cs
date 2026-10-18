using Carina.Domain.Segments;

namespace Carina.Domain.Encodings;

/// <summary>
/// An artefact that stands for its recording, the answer kept for the recording and its detection by the version
/// in use, the stretches its chapters are compared with standing as <see cref="Standing"/> says, and when its
/// chapters were last written into it again, if ever.
/// </summary>
public sealed record ChapterRewriteSubject(EncodeJob Job, RecordingDetection? Detection, RecordingAnswer? Answer, DateTime? LastRewrittenAt)
{
    public SegmentStanding Standing => SegmentStanding.Of(Answer, Detection);
}

public interface IChapterRewriteWorklist
{
    /// <summary>
    /// The artefacts whose chapters may no longer be the stretches their recording stands with: those of a
    /// recording with an answer standing that was not the one their chapters were decided from; those whose
    /// chapters came from an answer since withdrawn; and, where no answer stands, those of a recording detected
    /// by the version numbered <paramref name="modelVersion"/> after their chapters were last decided, or whose
    /// chapters were decided by another version or by nothing. None has a job of its recording waiting or running,
    /// none was written again since <paramref name="rewrittenSince"/> and none is one of <paramref name="passedOver"/>;
    /// the most recently answered, withdrawn or detected come first.
    /// </summary>
    Task<IReadOnlyList<ChapterRewriteSubject>> AwaitingAsync(
        int modelVersion,
        DateTime rewrittenSince,
        IReadOnlyCollection<EncodeJobId> passedOver,
        int atMost,
        CancellationToken cancellationToken);
}
