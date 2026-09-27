namespace Carina.Domain.Encodings;

/// <summary>
/// Where the chapters a run marked are kept. A job holds only what its last reading marked: a new
/// reading replaces the one before it. They are written before the encode that bakes them into the
/// artefact starts, and read back in the order they were marked in.
/// </summary>
public interface IEncodeChapterRepository
{
    Task RecordAsync(EncodeJobId jobId, IReadOnlyList<EncodeChapter> chapters, CancellationToken cancellationToken);

    Task<IReadOnlyList<EncodeChapter>> ListForJobAsync(EncodeJobId jobId, CancellationToken cancellationToken);
}
