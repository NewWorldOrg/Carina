using Carina.Api.Services;
using Carina.Domain.Auth;

namespace Carina.Api.Responder.Auth;

public sealed record OidcConfigResponder(
    bool Configured,
    string? DiscoveryUrl,
    string? ClientId,
    bool SecretHeld,
    IReadOnlyList<string> AllowedGroups,
    IReadOnlyList<string> AllowedHostedDomains,
    bool AdmitsEveryone,
    OidcReach Reach,
    string RedirectUri)
{
    public static OidcConfigResponder Of(OidcConfigView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new OidcConfigResponder(
            view.Configured,
            view.DiscoveryUrl,
            view.ClientId,
            view.SecretHeld,
            view.AllowedGroups,
            view.AllowedHostedDomains,
            view.AdmitsEveryone,
            view.Reach,
            view.RedirectUri);
    }
}
