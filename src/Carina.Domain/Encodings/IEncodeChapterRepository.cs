namespace Carina.Domain.Encodings;

/// <summary>
/// Where the chapters a run marked are kept. What a job holds is what its last reading marked and
/// nothing else: a job put back in the queue reads its source again, and the reading that follows
/// replaces the one before it rather than sitting beside it. They are written before the encode
/// that bakes them into the artefact starts, and read back in the order they were marked in — so
/// what a player is told is what the ledger holds, and nothing has to open the artefact to find
/// out.
/// </summary>
public interface IEncodeChapterRepository
{
    Task RecordAsync(EncodeJobId jobId, IReadOnlyList<EncodeChapter> chapters, CancellationToken cancellationToken);

    Task<IReadOnlyList<EncodeChapter>> ListForJobAsync(EncodeJobId jobId, CancellationToken cancellationToken);
}
