using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class SegmentModelVersionRepository(CarinaDbContext context) : ISegmentModelVersionRepository
{
    public async Task<SegmentModelVersion> ActiveAsync(CancellationToken cancellationToken)
        => await context.Set<SegmentModelVersion>()
               .AsNoTracking()
               .SingleOrDefaultAsync(version => version.State == SegmentModelVersionState.Active, cancellationToken)
           ?? throw new InvalidOperationException("No version of the model is in use; the migration that made the table puts the first one in use.");

    public async Task<SegmentModelVersion?> FindAsync(int number, CancellationToken cancellationToken)
        => await context.Set<SegmentModelVersion>()
            .AsNoTracking()
            .SingleOrDefaultAsync(version => version.Number == number, cancellationToken);
}
