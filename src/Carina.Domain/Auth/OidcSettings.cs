using Carina.Domain.Base;

namespace Carina.Domain.Auth;

public sealed class OidcSettings
{
    public const int TheOnlyRow = 1;

    public const int LongestDiscoveryUrl = 2048;

    public const int LongestClientId = 256;

    private OidcSettings()
    {
    }

    public int Id { get; private set; }

    public string? DiscoveryUrl { get; private set; }

    public string? ClientId { get; private set; }

    public ClientSecret? ClientSecret { get; private set; }

    public IReadOnlyList<string> AllowedGroups { get; private set; } = [];

    public IReadOnlyList<string> AllowedHostedDomains { get; private set; } = [];

    public DateTime UpdatedAt { get; private set; }

    public bool IsConfigured => DiscoveryUrl is not null && ClientId is not null && ClientSecret is not null;

    public OidcRestriction Restriction => OidcRestriction.Of(AllowedGroups, AllowedHostedDomains);

    public static OidcSettings Unconfigured(DateTime at) => Rehydrate(TheOnlyRow, null, null, null, at);

    public static OidcSettings Rehydrate(
        int id,
        string? discoveryUrl,
        string? clientId,
        ClientSecret? clientSecret,
        DateTime updatedAt,
        IEnumerable<string>? allowedGroups = null,
        IEnumerable<string>? allowedHostedDomains = null)
    {
        bool anything = discoveryUrl is not null || clientId is not null || clientSecret is not null;
        bool everything = discoveryUrl is not null && clientId is not null && clientSecret is not null;

        if (anything && !everything)
        {
            throw new ArgumentException(
                "An identity provider is reachable only with all three of its settings, so a row holds either all of them or none.",
                nameof(discoveryUrl));
        }

        return new OidcSettings
        {
            Id = id,
            DiscoveryUrl = discoveryUrl is null ? null : ValidatedDiscoveryUrl(discoveryUrl),
            ClientId = clientId is null ? null : ValidatedClientId(clientId),
            ClientSecret = clientSecret,
            AllowedGroups = OidcRestriction.Tidied(allowedGroups, nameof(allowedGroups)),
            AllowedHostedDomains = OidcRestriction.Tidied(
                allowedHostedDomains,
                nameof(allowedHostedDomains)),
            UpdatedAt = UtcTimes.Required(updatedAt, nameof(updatedAt)),
        };
    }

    public void Configure(string discoveryUrl, string clientId, ClientSecret? clientSecret, DateTime at)
    {
        UtcTimes.Required(at, nameof(at));
        ArgumentOutOfRangeException.ThrowIfLessThan(at, UpdatedAt, nameof(at));

        string discovery = ValidatedDiscoveryUrl(discoveryUrl);
        string client = ValidatedClientId(clientId);
        ClientSecret secret = clientSecret
            ?? ClientSecret
            ?? throw new InvalidOperationException(
                "The client secret is write-only, so configuring an identity provider for the first time has to carry one.");

        DiscoveryUrl = discovery;
        ClientId = client;
        ClientSecret = secret;
        UpdatedAt = at;
    }

    public void Restrict(
        IEnumerable<string>? allowedGroups,
        IEnumerable<string>? allowedHostedDomains,
        DateTime at)
    {
        UtcTimes.Required(at, nameof(at));
        ArgumentOutOfRangeException.ThrowIfLessThan(at, UpdatedAt, nameof(at));

        AllowedGroups = OidcRestriction.Tidied(allowedGroups, nameof(allowedGroups));
        AllowedHostedDomains = OidcRestriction.Tidied(
            allowedHostedDomains,
            nameof(allowedHostedDomains));
        UpdatedAt = at;
    }

    public void Clear(DateTime at)
    {
        UtcTimes.Required(at, nameof(at));
        ArgumentOutOfRangeException.ThrowIfLessThan(at, UpdatedAt, nameof(at));

        DiscoveryUrl = null;
        ClientId = null;
        ClientSecret = null;
        AllowedGroups = [];
        AllowedHostedDomains = [];
        UpdatedAt = at;
    }

    private static string ValidatedDiscoveryUrl(string discoveryUrl)
    {
        ArgumentNullException.ThrowIfNull(discoveryUrl);

        if (!Uri.TryCreate(discoveryUrl, UriKind.Absolute, out Uri? parsed) || parsed.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "A discovery document is fetched over https, and anything else would put the client secret on the wire in the clear.",
                nameof(discoveryUrl));
        }

        if (discoveryUrl.Length > LongestDiscoveryUrl)
        {
            throw new ArgumentOutOfRangeException(
                nameof(discoveryUrl),
                discoveryUrl.Length,
                $"A discovery URL is at most {LongestDiscoveryUrl} characters.");
        }

        return discoveryUrl;
    }

    private static string ValidatedClientId(string clientId)
    {
        ArgumentNullException.ThrowIfNull(clientId);

        string trimmed = clientId.Trim();

        if (trimmed.Length == 0)
        {
            throw new ArgumentException(
                "A client id is what the identity provider registered, so it cannot be blank.",
                nameof(clientId));
        }

        if (trimmed.Length > LongestClientId)
        {
            throw new ArgumentOutOfRangeException(
                nameof(clientId),
                trimmed.Length,
                $"A client id is at most {LongestClientId} characters.");
        }

        return trimmed;
    }
}
