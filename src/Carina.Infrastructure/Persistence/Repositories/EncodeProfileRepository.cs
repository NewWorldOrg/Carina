using Carina.Domain.Encodings;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class EncodeProfileRepository(CarinaDbContext context) : IEncodeProfileRepository
{
    public async Task<EncodeProfile?> FindAsync(EncodeProfileId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        return await context.Set<EncodeProfile>().SingleOrDefaultAsync(profile => profile.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<EncodeProfile>> ListAsync(CancellationToken cancellationToken)
        => await context.Set<EncodeProfile>()
            .OrderBy(profile => profile.DefinedAt)
            .ThenBy(profile => profile.Id)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(EncodeProfile profile, CancellationToken cancellationToken)
    {
        context.Add(profile);

        await context.SaveChangesAsync(cancellationToken);
    }
}
