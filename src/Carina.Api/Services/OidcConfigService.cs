using Carina.Api.Common;
using Carina.Domain.Auth;

namespace Carina.Api.Services;

public sealed class OidcConfigService(
    IOidcSettingsRepository settings,
    IOidcDirectory directory,
    IOidcGateway gateway,
    IOidcReachability reachability,
    TimeProvider clock)
{
    public const string TheProviderDidNotAnswer =
        "The discovery document could not be read, so nothing was saved. Register the redirect URI shown here "
        + "with the identity provider first, then check the discovery URL.";

    public const string AFirstSaveCarriesItsSecret =
        "The client secret is write-only, so the first save of an identity provider has to carry one.";

    public async Task<ServiceResult<OidcConfigView>> ReadAsync(
        string redirectUri,
        CancellationToken cancellationToken)
    {
        OidcSettings held = await settings.FindAsync(cancellationToken)
                            ?? OidcSettings.Unconfigured(clock.GetUtcNow().UtcDateTime);

        return ServiceResult<OidcConfigView>.Success(
            OidcConfigView.Of(held, reachability.State, redirectUri));
    }

    public async Task<ServiceResult<OidcConfigView>> SaveAsync(
        OidcConfigChange change,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        DateTime now = clock.GetUtcNow().UtcDateTime;
        OidcSettings held = await settings.FindAsync(cancellationToken) ?? OidcSettings.Unconfigured(now);

        if (Named(change.DiscoveryUrl) is not { } discoveryUrl || Named(change.ClientId) is not { } clientId)
        {
            held.Clear(now);
            held.Restrict(change.AllowedGroups, change.AllowedHostedDomains, now);

            return await SavedAsync(held, redirectUri, cancellationToken);
        }

        ClientSecret? offered = Named(change.ClientSecret) is { } secret ? new ClientSecret(secret) : null;

        if (offered is null && held.ClientSecret is null)
        {
            return ServiceResult<OidcConfigView>.Failure(AFirstSaveCarriesItsSecret);
        }

        var candidate = OidcSettings.Unconfigured(now);

        try
        {
            candidate.Configure(discoveryUrl, clientId, offered ?? held.ClientSecret, now);
            candidate.Restrict(change.AllowedGroups, change.AllowedHostedDomains, now);
        }
        catch (ArgumentException refusal)
        {
            return ServiceResult<OidcConfigView>.Failure(refusal.Message);
        }

        if (await gateway.ReachAsync(candidate.DiscoveryUrl!, cancellationToken) is null)
        {
            return ServiceResult<OidcConfigView>.Failure(TheProviderDidNotAnswer);
        }

        held.Configure(discoveryUrl, clientId, offered, now);
        held.Restrict(change.AllowedGroups, change.AllowedHostedDomains, now);

        return await SavedAsync(held, redirectUri, cancellationToken);
    }

    private static string? Named(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<ServiceResult<OidcConfigView>> SavedAsync(
        OidcSettings held,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        await settings.SaveAsync(held, cancellationToken);
        await directory.ProbeAsync(held, cancellationToken);

        return ServiceResult<OidcConfigView>.Success(
            OidcConfigView.Of(held, reachability.State, redirectUri));
    }
}
