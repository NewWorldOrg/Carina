namespace Carina.Domain.Auth;

public interface IOidcDirectory
{
    Task<OidcEndpoints?> ForAsync(OidcSettings settings, CancellationToken cancellationToken);

    Task<OidcEndpoints?> ProbeAsync(OidcSettings settings, CancellationToken cancellationToken);
}
