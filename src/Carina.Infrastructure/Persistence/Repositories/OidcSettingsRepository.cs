using Carina.Domain.Auth;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class OidcSettingsRepository(CarinaDbContext context) : IOidcSettingsRepository
{
    public async Task<OidcSettings?> FindAsync(CancellationToken cancellationToken)
        => await context.Set<OidcSettings>()
            .FirstOrDefaultAsync(settings => settings.Id == OidcSettings.TheOnlyRow, cancellationToken);

    public async Task SaveAsync(OidcSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (context.Entry(settings).State is EntityState.Detached)
        {
            await context.AddAsync(settings, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
