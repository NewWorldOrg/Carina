using Carina.Domain.Base;

namespace Carina.Domain.Auth;

public sealed class PendingOidcLogin
{
    private PendingOidcLogin(
        string state,
        string nonce,
        PkceChallenge pkce,
        string browserMark,
        string returnPath,
        DateTime startedAt)
    {
        State = state;
        Nonce = nonce;
        Pkce = pkce;
        BrowserMark = browserMark;
        ReturnPath = returnPath;
        StartedAt = startedAt;
    }

    public string State { get; }

    public string Nonce { get; }

    public PkceChallenge Pkce { get; }

    public string BrowserMark { get; }

    public string ReturnPath { get; }

    public DateTime StartedAt { get; }

    public static PendingOidcLogin Begin(string browserMark, string returnPath, DateTime at)
        => new(
            Unguessable.Issue(),
            Unguessable.Issue(),
            PkceChallenge.Issue(),
            Unguessable.Validated(browserMark, nameof(browserMark)),
            ValidatedReturnPath(returnPath),
            UtcTimes.Required(at, nameof(at)));

    public bool HasLapsed(DateTime now, OidcLoginPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        UtcTimes.Required(now, nameof(now));

        return now >= StartedAt + policy.HandshakeLifetime;
    }

    public bool BelongsTo(string? browserMark) => Unguessable.Same(BrowserMark, browserMark);

    private static string ValidatedReturnPath(string returnPath)
    {
        ArgumentNullException.ThrowIfNull(returnPath);

        if (returnPath.Length == 0 || returnPath[0] != '/')
        {
            throw new ArgumentException(
                "A handshake carries the caller back to a path inside this host, and nothing else is a place to return to.",
                nameof(returnPath));
        }

        return returnPath;
    }
}
