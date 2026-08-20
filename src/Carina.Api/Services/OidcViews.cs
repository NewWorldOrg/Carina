using Carina.Api.Authentication;
using Carina.Domain.Auth;

namespace Carina.Api.Services;

public sealed record OidcStartAttempt(string? BrowserMark, string? ReturnTo, string ArrivedAt);

public sealed record OidcStart(Uri Authorize, string BrowserMark, TimeSpan MarkLifetime);

public sealed record OidcArrivalAttempt(
    string? State,
    string? Code,
    string? BrowserMark,
    string ArrivedAt,
    string DeviceLabel);

public sealed record OidcArrival(AuthSession Session, string ReturnPath, TimeSpan SessionLifetime);

public sealed record OidcConfigChange(
    string? DiscoveryUrl,
    string? ClientId,
    string? ClientSecret,
    IReadOnlyList<string>? AllowedGroups,
    IReadOnlyList<string>? AllowedHostedDomains);

public sealed record OidcConfigView(
    bool Configured,
    string? DiscoveryUrl,
    string? ClientId,
    bool SecretHeld,
    IReadOnlyList<string> AllowedGroups,
    IReadOnlyList<string> AllowedHostedDomains,
    bool AdmitsEveryone,
    OidcReach Reach,
    string RedirectUri,
    bool RedirectUriGuessed)
{
    public static OidcConfigView Of(OidcSettings settings, OidcReach reach, PublicRedirectUri redirect)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(redirect);

        return new OidcConfigView(
            settings.IsConfigured,
            settings.DiscoveryUrl,
            settings.ClientId,
            settings.ClientSecret is not null,
            settings.AllowedGroups,
            settings.AllowedHostedDomains,
            settings.Restriction.AdmitsEveryone,
            reach,
            redirect.Value,
            redirect.Guessed);
    }
}

public sealed record SignInOptionsView(bool IdentityProvider, string? ProviderName, OidcReach Reach)
{
    public static SignInOptionsView Of(OidcSettings settings, OidcReach reach)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.IsConfigured
            ? new SignInOptionsView(true, settings.ProviderName, reach)
            : new SignInOptionsView(false, null, OidcReach.NotConfigured);
    }
}

public sealed record HealthView(string Status, IReadOnlyList<string> Degraded)
{
    public const string Alive = "ok";

    public const string TheIdentityProvider = "oidc";

    public static HealthView Of(OidcReach reach)
        => new(Alive, reach is OidcReach.OutOfReach ? [TheIdentityProvider] : []);
}
