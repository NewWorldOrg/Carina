using Carina.Domain.Segments;

namespace Carina.Domain.Encodings;

public sealed record ChapterRewriteSubject(EncodeJob Job, RecordingDetection Detection, DateTime? LastRewrittenAt)
{
    public SegmentStanding Standing => SegmentStanding.Of(Detection);
}

public interface IChapterRewriteWorklist
{
    Task<IReadOnlyList<ChapterRewriteSubject>> AwaitingAsync(
        int modelVersion,
        DateTime rewrittenSince,
        IReadOnlyCollection<EncodeJobId> passedOver,
        int atMost,
        CancellationToken cancellationToken);
}
