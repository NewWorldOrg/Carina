using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Domain.Auth;

namespace Carina.Api.Services;

public sealed class OidcConfigService(
    IOidcSettingsRepository settings,
    IOidcDirectory directory,
    IOidcGateway gateway,
    IOidcReachability reachability,
    PublicOrigin origin,
    TimeProvider clock)
{
    public const string TheProviderDidNotAnswer =
        "The discovery document could not be read, so nothing was saved. Register the redirect URI shown here "
        + "with the identity provider first, then check the discovery URL.";

    public const string AFirstSaveCarriesItsSecret =
        "The client secret is write-only, so the first save of an identity provider has to carry one.";

    public const string TheSecretWasLost =
        "The client secret that was kept can no longer be opened with the keys this installation holds, so it has to be entered again.";

    public async Task<ServiceResult<OidcConfigView>> ReadAsync(
        string arrivedAt,
        CancellationToken cancellationToken)
    {
        OidcSettings held = await settings.FindAsync(cancellationToken)
                            ?? OidcSettings.Unconfigured(clock.GetUtcNow().UtcDateTime);

        return ServiceResult<OidcConfigView>.Success(
            OidcConfigView.Of(held, reachability.State, origin.RedirectUriFor(arrivedAt)));
    }

    public async Task<ServiceResult<SignInOptionsView>> ReadSignInOptionsAsync(CancellationToken cancellationToken)
    {
        OidcSettings held = await settings.FindAsync(cancellationToken)
                            ?? OidcSettings.Unconfigured(clock.GetUtcNow().UtcDateTime);

        return ServiceResult<SignInOptionsView>.Success(SignInOptionsView.Of(held, reachability.State));
    }

    public async Task<ServiceResult<OidcConfigView, OidcConfigRefusal>> SaveAsync(
        OidcConfigChange change,
        string arrivedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        DateTime now = clock.GetUtcNow().UtcDateTime;
        OidcSettings held = await settings.FindAsync(cancellationToken) ?? OidcSettings.Unconfigured(now);

        if (Named(change.DiscoveryUrl) is not { } discoveryUrl || Named(change.ClientId) is not { } clientId)
        {
            try
            {
                held.Clear(now);
                held.Restrict(change.AllowedGroups, change.AllowedHostedDomains, now);
            }
            catch (ArgumentException refusal) when (Refusing(refusal.ParamName) is { } kind)
            {
                return ServiceResult<OidcConfigView, OidcConfigRefusal>.Failure(refusal.Message, kind);
            }

            return await SavedAsync(held, arrivedAt, cancellationToken);
        }

        ClientSecret? offered = Named(change.ClientSecret) is { } secret ? new ClientSecret(secret) : null;

        if (offered is null && held.SecretLost)
        {
            return ServiceResult<OidcConfigView, OidcConfigRefusal>.Failure(
                TheSecretWasLost,
                OidcConfigRefusal.SecretLost);
        }

        if (offered is null && held.ClientSecret is null)
        {
            return ServiceResult<OidcConfigView, OidcConfigRefusal>.Failure(
                AFirstSaveCarriesItsSecret,
                OidcConfigRefusal.SecretRequired);
        }

        OidcSettings candidate = OidcSettings.Unconfigured(now);

        try
        {
            candidate.Configure(discoveryUrl, clientId, offered ?? held.ClientSecret, now);
            candidate.Restrict(change.AllowedGroups, change.AllowedHostedDomains, now);
        }
        catch (ArgumentException refusal) when (Refusing(refusal.ParamName) is { } kind)
        {
            return ServiceResult<OidcConfigView, OidcConfigRefusal>.Failure(refusal.Message, kind);
        }

        if (await gateway.ReachAsync(candidate.DiscoveryUrl!, cancellationToken) is null)
        {
            return ServiceResult<OidcConfigView, OidcConfigRefusal>.Failure(
                TheProviderDidNotAnswer,
                OidcConfigRefusal.ProviderUnreachable);
        }

        held.Configure(discoveryUrl, clientId, offered, now);
        held.Restrict(change.AllowedGroups, change.AllowedHostedDomains, now);

        return await SavedAsync(held, arrivedAt, cancellationToken);
    }

    private static OidcConfigRefusal? Refusing(string? setting)
        => setting switch
        {
            "discoveryUrl" => OidcConfigRefusal.DiscoveryUrlInvalid,
            "clientId" => OidcConfigRefusal.ClientIdInvalid,
            "allowedGroups" or "allowedHostedDomains" => OidcConfigRefusal.RestrictionInvalid,
            _ => null,
        };

    private static string? Named(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<ServiceResult<OidcConfigView, OidcConfigRefusal>> SavedAsync(
        OidcSettings held,
        string arrivedAt,
        CancellationToken cancellationToken)
    {
        await settings.SaveAsync(held, cancellationToken);
        await directory.ProbeAsync(held, cancellationToken);

        return ServiceResult<OidcConfigView, OidcConfigRefusal>.Success(
            OidcConfigView.Of(held, reachability.State, origin.RedirectUriFor(arrivedAt)));
    }
}
