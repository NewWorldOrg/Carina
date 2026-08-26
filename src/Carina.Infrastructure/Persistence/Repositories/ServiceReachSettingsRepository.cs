using Carina.Domain.Channels;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class ServiceReachSettingsRepository(CarinaDbContext context, TimeProvider clock)
    : IServiceReachSettingsRepository
{
    public async Task<ServiceReachSettings> ReadAsync(CancellationToken cancellationToken)
        => await context.Set<ServiceReachSettings>()
               .FirstOrDefaultAsync(settings => settings.Id == ServiceReachSettings.TheOnlyRow, cancellationToken)
           ?? ServiceReachSettings.Default(clock.GetUtcNow().UtcDateTime);

    public async Task SaveAsync(ServiceReachSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (context.Entry(settings).State is EntityState.Detached)
        {
            await context.AddAsync(settings, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
