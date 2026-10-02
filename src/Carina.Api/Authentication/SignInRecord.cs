using Carina.Domain.Auth;

using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Authentication;

/// <summary>
/// Writes one line for each thing that happens at the gate and at the ways in.
/// A line says what arrived and whether a cookie came with it, and never carries a value.
/// </summary>
public sealed class SignInRecord(ILogger<SignInRecord> logger, TimeProvider clock)
{
    public const string Line =
        "Sign-in {Moment} at {Method} {Route}: session cookie {SessionCookie}, handshake cookie {HandshakeCookie}, "
        + "signed in by {SignInMethod} on {Device}, reason {Reason}, fetch site {FetchSite}, agent {UserAgent}.";

    public const string Carried = "carried";

    public const string Absent = "absent";

    public const string Unsaid = "-";

    public const string NoRoute = "(no route)";

    public const string FetchSite = "Sec-Fetch-Site";

    public const int LongestAgent = 200;

    public const int LongestRoute = 120;

    public const int LongestWord = 20;

    public const int MostAgentsRemembered = 256;

    public static readonly TimeSpan Quiet = TimeSpan.FromMinutes(1);

    private readonly Dictionary<(SignInMoment Moment, string Agent), DateTime> lastSaid = [];

    private readonly Lock turn = new();

    public void Write(
        HttpContext context,
        SignInMoment moment,
        AuthMethod? method = null,
        string? device = null,
        string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        HttpRequest request = context.Request;
        string agent = Plain(request.Headers.UserAgent.ToString(), LongestAgent);

        if (IsSaidOncePerQuiet(moment) && !MaySay(moment, agent))
        {
            return;
        }

        logger.Log(
            LevelOf(moment),
            Line,
            moment,
            Plain(request.Method, LongestWord),
            RouteOf(context),
            SessionCookie.CarriedBy(request) is null ? Absent : Carried,
            request.Cookies.ContainsKey(OidcHandshake.MarkName) ? Carried : Absent,
            method?.ToString() ?? Unsaid,
            Plain(device, AuthSession.LongestDeviceLabel),
            Plain(reason, LongestRoute),
            Plain(request.Headers[FetchSite].ToString(), LongestWord),
            agent);
    }

    private static bool IsSaidOncePerQuiet(SignInMoment moment)
        => moment is SignInMoment.RefusedWithoutASessionCookie
            or SignInMoment.TheSessionCookieNamedNoSession
            or SignInMoment.TheSessionHadExpired
            or SignInMoment.TheSessionHadBeenRevoked;

    private static LogLevel LevelOf(SignInMoment moment)
        => moment is SignInMoment.TheWayToTheProviderCouldNotBeOpened
            or SignInMoment.TheWayBackFromTheProviderWasRefused
            ? LogLevel.Warning
            : LogLevel.Information;

    private static string RouteOf(HttpContext context)
        => context.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: { Length: > 0 } pattern }
            ? Plain($"/{pattern.TrimStart('/')}", LongestRoute)
            : NoRoute;

    private static string Plain(string? said, int longest)
    {
        if (string.IsNullOrWhiteSpace(said))
        {
            return Unsaid;
        }

        string plain = new string([.. said.Select(letter => letter is >= ' ' and <= '~' ? letter : '?')]).Trim();

        return plain.Length > longest ? plain[..longest] : plain;
    }

    private bool MaySay(SignInMoment moment, string agent)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;

        lock (turn)
        {
            if (lastSaid.TryGetValue((moment, agent), out DateTime said) && now - said < Quiet)
            {
                return false;
            }

            if (lastSaid.Count >= MostAgentsRemembered)
            {
                foreach ((SignInMoment Moment, string Agent) stale in lastSaid
                             .Where(entry => now - entry.Value >= Quiet)
                             .Select(entry => entry.Key)
                             .ToList())
                {
                    lastSaid.Remove(stale);
                }
            }

            if (lastSaid.Count >= MostAgentsRemembered && !lastSaid.ContainsKey((moment, agent)))
            {
                return false;
            }

            lastSaid[(moment, agent)] = now;

            return true;
        }
    }
}
