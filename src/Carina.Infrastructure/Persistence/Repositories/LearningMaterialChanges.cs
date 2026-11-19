using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class LearningMaterialChanges(CarinaDbContext context) : ILearningMaterialChanges
{
    public async Task<DateTime?> LastChangedAsync(CancellationToken cancellationToken)
    {
        DateTime?[] moments =
        [
            await context.Set<CatalogEntry>().MaxAsync(entry => (DateTime?)entry.UpdatedAt, cancellationToken),
            await context.Set<CatalogOccurrence>().MaxAsync(occurrence => (DateTime?)occurrence.MatchedAt, cancellationToken),
            await context.Set<SkipEvent>().MaxAsync(skip => (DateTime?)skip.HappenedAt, cancellationToken),
            await context.Set<SeekEvent>().MaxAsync(seek => (DateTime?)seek.HappenedAt, cancellationToken),
            await context.Set<LearningExtraction>()
                .Where(record => record.State == LearningExtractionState.Done)
                .MaxAsync(record => (DateTime?)record.UpdatedAt, cancellationToken),
        ];

        return moments.Max();
    }
}
