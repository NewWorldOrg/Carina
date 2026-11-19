namespace Carina.Domain.Encodings;

public interface IEncodeChapterRepository
{
    Task RecordAsync(EncodeJobId jobId, IReadOnlyList<EncodeChapter> chapters, CancellationToken cancellationToken);

    Task<IReadOnlyList<EncodeChapter>> ListForJobAsync(EncodeJobId jobId, CancellationToken cancellationToken);
}
