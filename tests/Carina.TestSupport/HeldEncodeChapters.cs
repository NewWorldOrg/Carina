using Carina.Domain.Encodings;

namespace Carina.TestSupport;

public sealed class HeldEncodeChapters : IEncodeChapterRepository
{
    public List<EncodeChapter> Chapters { get; } = [];

    public Action<IReadOnlyList<EncodeChapter>>? WhenRecording { get; set; }

    public Task RecordAsync(EncodeJobId jobId, IReadOnlyList<EncodeChapter> chapters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(chapters);

        WhenRecording?.Invoke(chapters);
        Chapters.RemoveAll(chapter => chapter.JobId.Equals(jobId));
        Chapters.AddRange(chapters);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<EncodeChapter>> ListForJobAsync(EncodeJobId jobId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobId);

        IReadOnlyList<EncodeChapter> marked =
        [
            .. Chapters.Where(chapter => chapter.JobId.Equals(jobId)).OrderBy(chapter => chapter.Ordinal),
        ];

        return Task.FromResult(marked);
    }
}
