using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Domain.Auth;

namespace Carina.Api.Services;

public sealed class OidcLoginService(
    IOidcSettingsRepository settings,
    IOidcDirectory directory,
    IOidcGateway gateway,
    IPendingOidcLoginStore handshakes,
    IAuthSessionRepository sessions,
    PublicOrigin origin,
    OidcLoginPolicy policy,
    SessionPolicy sessionPolicy,
    TimeProvider clock,
    ILogger<OidcLoginService> logger)
{
    public const string TheSameRefusalForEveryFailedSignIn =
        "Signing in through the identity provider did not work.";

    public const string Scope = "openid profile email";

    public async Task<ServiceResult<OidcStart, OidcRefusal>> StartAsync(
        OidcStartAttempt attempt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        OidcSettings? held = await settings.FindAsync(cancellationToken);

        if (held?.IsConfigured is not true)
        {
            return Refused<OidcStart>(OidcRefusal.NoIdentityProviderIsConfigured);
        }

        if (await directory.ForAsync(held, cancellationToken) is not { } endpoints)
        {
            return Refused<OidcStart>(OidcRefusal.TheIdentityProviderIsOutOfReach);
        }

        string mark = Unguessable.IsOne(attempt.BrowserMark) ? attempt.BrowserMark! : Unguessable.Issue();
        PendingOidcLogin pending = PendingOidcLogin.Begin(
            mark,
            LoginRedirect.Within(attempt.ReturnTo),
            clock.GetUtcNow().UtcDateTime);

        handshakes.Hold(pending);

        return ServiceResult<OidcStart, OidcRefusal>.Success(
            new OidcStart(
                AuthorizeUri(endpoints, held.ClientId!, origin.RedirectUriFor(attempt.ArrivedAt).Value, pending),
                mark,
                policy.HandshakeLifetime));
    }

    public async Task<ServiceResult<OidcArrival, OidcRefusal>> CompleteAsync(
        OidcArrivalAttempt attempt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        OidcSettings? held = await settings.FindAsync(cancellationToken);

        if (held?.IsConfigured is not true)
        {
            return Refused<OidcArrival>(OidcRefusal.NoIdentityProviderIsConfigured);
        }

        if (attempt.State is not { Length: > 0 } state || handshakes.Take(state) is not { } pending)
        {
            return Refused<OidcArrival>(OidcRefusal.NoHandshakeAnsweredToThatState);
        }

        DateTime now = clock.GetUtcNow().UtcDateTime;

        if (!pending.BelongsTo(attempt.BrowserMark))
        {
            return Refused<OidcArrival>(OidcRefusal.TheHandshakeBelongsToAnotherBrowser);
        }

        if (pending.HasLapsed(now, policy))
        {
            return Refused<OidcArrival>(OidcRefusal.TheHandshakeLapsed);
        }

        if (attempt.Code is not { Length: > 0 } code)
        {
            return Refused<OidcArrival>(OidcRefusal.TheCodeWasRefused);
        }

        if (await directory.ForAsync(held, cancellationToken) is not { } endpoints)
        {
            return Refused<OidcArrival>(OidcRefusal.TheIdentityProviderIsOutOfReach);
        }

        string? idToken = await gateway.ExchangeAsync(
            endpoints,
            new OidcCodeExchange(
                held.ClientId!,
                held.ClientSecret!,
                code,
                origin.RedirectUriFor(attempt.ArrivedAt).Value,
                pending.Pkce.Verifier),
            cancellationToken);

        if (idToken is null)
        {
            return Refused<OidcArrival>(OidcRefusal.TheCodeWasRefused);
        }

        if (await gateway.ReadAsync(endpoints, idToken, cancellationToken) is not { } claims)
        {
            return Refused<OidcArrival>(OidcRefusal.TheIdTokenDidNotVerify);
        }

        OidcRefusal answered = new IdTokenExpectation(endpoints.Issuer, held.ClientId!, pending.Nonce)
            .Refuses(claims, now, policy.ClockSkew);

        if (answered is not OidcRefusal.None)
        {
            return Refused<OidcArrival>(answered);
        }

        OidcRefusal allowed = held.Restriction.Refuses(claims);

        if (allowed is not OidcRefusal.None)
        {
            return Refused<OidcArrival>(allowed);
        }

        AuthSession session = AuthSession.Start(
            SessionId.Issue(),
            new Subject(claims.Subject),
            claims.DisplayName,
            AuthMethod.Oidc,
            attempt.DeviceLabel,
            now);

        await sessions.SaveAsync(session, cancellationToken);

        return ServiceResult<OidcArrival, OidcRefusal>.Success(
            new OidcArrival(session, pending.ReturnPath, sessionPolicy.AbsoluteLifetime));
    }

    private static Uri AuthorizeUri(
        OidcEndpoints endpoints,
        string clientId,
        string redirectUri,
        PendingOidcLogin pending)
    {
        var asked = new List<KeyValuePair<string, string>>
        {
            new("response_type", "code"),
            new("client_id", clientId),
            new("redirect_uri", redirectUri),
            new("scope", Scope),
            new("state", pending.State),
            new("nonce", pending.Nonce),
            new("code_challenge", pending.Pkce.Challenge),
            new("code_challenge_method", PkceChallenge.Method),
        };

        string query = string.Join(
            '&',
            asked.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        string separator = string.IsNullOrEmpty(endpoints.Authorization.Query) ? "?" : "&";

        return new Uri($"{endpoints.Authorization}{separator}{query}");
    }

    private ServiceResult<T, OidcRefusal> Refused<T>(OidcRefusal refusal)
    {
        logger.LogWarning(
            "Signing in through the identity provider was refused: {Refusal}. The caller was told only that it did not work.",
            refusal);

        return ServiceResult<T, OidcRefusal>.Failure(TheSameRefusalForEveryFailedSignIn, refusal);
    }
}
