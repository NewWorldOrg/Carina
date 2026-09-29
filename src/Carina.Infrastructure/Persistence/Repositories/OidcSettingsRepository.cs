using Carina.Domain.Auth;
using Carina.Infrastructure.Auth;
using Carina.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class OidcSettingsRepository(CarinaDbContext context, ClientSecretSeal seal) : IOidcSettingsRepository
{
    public async Task<OidcSettings?> FindAsync(CancellationToken cancellationToken)
    {
        OidcSettings? settings = await context.Set<OidcSettings>()
            .FirstOrDefaultAsync(settings => settings.Id == OidcSettings.TheOnlyRow, cancellationToken);

        if (settings is { DiscoveryUrl: not null, ClientSecret: null })
        {
            settings.ReadBack(Opened(context.Entry(settings)));
        }

        return settings;
    }

    public async Task SaveAsync(OidcSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (context.Entry(settings).State is EntityState.Detached)
        {
            await context.AddAsync(settings, cancellationToken);
        }

        Seal(context.Entry(settings));

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> SealAnUnsealedSecretAsync(CancellationToken cancellationToken)
    {
        if (await FindAsync(cancellationToken) is not { } settings
            || context.Entry(settings).Property<string?>(OidcSettingsConfiguration.UnsealedSecret).CurrentValue is null)
        {
            return false;
        }

        await SaveAsync(settings, cancellationToken);

        return true;
    }

    private ClientSecret? Opened(EntityEntry<OidcSettings> entry)
    {
        string? held = entry.Property<string?>(OidcSettingsConfiguration.SealedSecret).CurrentValue;

        if (held is not null)
        {
            return seal.Open(held);
        }

        string? unsealed = entry.Property<string?>(OidcSettingsConfiguration.UnsealedSecret).CurrentValue;

        return unsealed is null ? null : new ClientSecret(unsealed);
    }

    private void Seal(EntityEntry<OidcSettings> entry)
    {
        OidcSettings settings = entry.Entity;

        if (settings.ClientSecret is not null)
        {
            entry.Property<string?>(OidcSettingsConfiguration.SealedSecret).CurrentValue = seal.Seal(settings.ClientSecret);
            entry.Property<string?>(OidcSettingsConfiguration.UnsealedSecret).CurrentValue = null;
        }
        else if (settings.DiscoveryUrl is null)
        {
            entry.Property<string?>(OidcSettingsConfiguration.SealedSecret).CurrentValue = null;
            entry.Property<string?>(OidcSettingsConfiguration.UnsealedSecret).CurrentValue = null;
        }
    }
}
