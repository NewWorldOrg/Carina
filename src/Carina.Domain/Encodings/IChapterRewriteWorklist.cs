using Carina.Domain.Segments;

namespace Carina.Domain.Encodings;

/// <summary>
/// An artefact that stands for its recording, the detection of the recording by the version in use its chapters
/// are compared with, standing as <see cref="Standing"/> says, and when its chapters were last written into it
/// again, if ever.
/// </summary>
public sealed record ChapterRewriteSubject(EncodeJob Job, RecordingDetection Detection, DateTime? LastRewrittenAt)
{
    public SegmentStanding Standing => SegmentStanding.Of(Detection);
}

public interface IChapterRewriteWorklist
{
    /// <summary>
    /// The artefacts that stand for a recording detected by the version numbered <paramref name="modelVersion"/>
    /// after their chapters were last decided, or whose chapters were decided by another version or by nothing,
    /// with no job of that recording waiting or running, none of them written again since
    /// <paramref name="rewrittenSince"/> and none of <paramref name="passedOver"/>, most recently detected first.
    /// </summary>
    Task<IReadOnlyList<ChapterRewriteSubject>> AwaitingAsync(
        int modelVersion,
        DateTime rewrittenSince,
        IReadOnlyCollection<EncodeJobId> passedOver,
        int atMost,
        CancellationToken cancellationToken);
}
