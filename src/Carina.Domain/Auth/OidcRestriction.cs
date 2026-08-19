namespace Carina.Domain.Auth;

public sealed class OidcRestriction
{
    public const int LongestEntry = 256;

    public const int MostEntries = 64;

    private OidcRestriction(IReadOnlyList<string> groups, IReadOnlyList<string> hostedDomains)
    {
        Groups = groups;
        HostedDomains = hostedDomains;
    }

    public static OidcRestriction None { get; } = new([], []);

    public IReadOnlyList<string> Groups { get; }

    public IReadOnlyList<string> HostedDomains { get; }

    public bool AdmitsEveryone => Groups.Count == 0 && HostedDomains.Count == 0;

    public static OidcRestriction Of(IEnumerable<string>? groups, IEnumerable<string>? hostedDomains)
    {
        IReadOnlyList<string> named = Tidied(groups, nameof(groups));
        IReadOnlyList<string> domains = Tidied(hostedDomains, nameof(hostedDomains));

        return named.Count == 0 && domains.Count == 0 ? None : new OidcRestriction(named, domains);
    }

    public static IReadOnlyList<string> Tidied(IEnumerable<string>? entries, string name)
    {
        if (entries is null)
        {
            return [];
        }

        string[] kept =
        [
            .. entries
                .Select(entry => entry?.Trim() ?? string.Empty)
                .Where(entry => entry.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];

        if (kept.Length > MostEntries)
        {
            throw new ArgumentOutOfRangeException(
                name,
                kept.Length,
                $"A restriction names at most {MostEntries} entries.");
        }

        foreach (string entry in kept)
        {
            if (entry.Length > LongestEntry)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    entry.Length,
                    $"An entry in a restriction is at most {LongestEntry} characters.");
            }

            if (entry.Any(char.IsControl))
            {
                throw new ArgumentException(
                    "An entry in a restriction reaches screens and a query, so it carries no control characters.",
                    name);
            }
        }

        return kept;
    }

    public OidcRefusal Refuses(OidcClaims claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        if (AdmitsEveryone)
        {
            return OidcRefusal.None;
        }

        if (Groups.Count > 0 && claims.GroupsOverflowed)
        {
            return OidcRefusal.TheGroupsOverflowedOutOfTheToken;
        }

        if (Groups.Any(named => claims.Groups.Contains(named, StringComparer.OrdinalIgnoreCase)))
        {
            return OidcRefusal.None;
        }

        if (claims.HostedDomain is { } hosted
            && HostedDomains.Contains(hosted, StringComparer.OrdinalIgnoreCase))
        {
            return OidcRefusal.None;
        }

        return OidcRefusal.OutsideEveryAllowedGroupAndDomain;
    }
}
