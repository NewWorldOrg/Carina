namespace Carina.Domain.Auth;

public interface IOidcSettingsRepository
{
    Task<OidcSettings?> FindAsync(CancellationToken cancellationToken);

    Task SaveAsync(OidcSettings settings, CancellationToken cancellationToken);
}
