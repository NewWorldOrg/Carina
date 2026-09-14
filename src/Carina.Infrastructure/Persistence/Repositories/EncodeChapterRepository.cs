using Carina.Domain.Encodings;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class EncodeChapterRepository(CarinaDbContext context) : IEncodeChapterRepository
{
    public async Task RecordAsync(EncodeJobId jobId, IReadOnlyList<EncodeChapter> chapters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(chapters);

        if (chapters.Any(chapter => !chapter.JobId.Equals(jobId)))
        {
            throw new ArgumentException("A job is written the chapters of its own reading.", nameof(chapters));
        }

        await context.Set<EncodeChapter>()
            .Where(chapter => chapter.JobId == jobId)
            .ExecuteDeleteAsync(cancellationToken);

        if (chapters.Count is 0)
        {
            return;
        }

        context.AddRange(chapters);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EncodeChapter>> ListForJobAsync(EncodeJobId jobId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobId);

        return await context.Set<EncodeChapter>()
            .Where(chapter => chapter.JobId == jobId)
            .OrderBy(chapter => chapter.Ordinal)
            .ToListAsync(cancellationToken);
    }
}
