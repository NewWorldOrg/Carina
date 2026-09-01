namespace Carina.Architecture.Tests;

public static class AuthenticationBypasses
{
    public static IReadOnlyList<string> EdgeIdentityHeaders { get; } =
    [
        "X-Forwarded-User",
        "X-Forwarded-Email",
        "X-Forwarded-Preferred-Username",
        "X-Forwarded-Groups",
        "X-Auth-Request-User",
        "X-Auth-Request-Email",
        "X-Auth-Request-Groups",
    ];

    public static IReadOnlyList<string> AnonymityAttributes { get; } =
    [
        "AllowAnonymous",
    ];

    public static IReadOnlyList<string> AskingWhoIsCalling { get; } =
    [
        "AddAuthentication",
        "UseAuthentication",
        "AddAuthorization",
        "UseAuthorization",
        "AuthenticationHandler",
        "[Authorize]",
        "WWW-Authenticate",
    ];

    public static IReadOnlyList<string> IdentityProviderSignOut { get; } =
    [
        "end_session_endpoint",
        "EndSessionEndpoint",
    ];

    public static IReadOnlyList<string> ClientSecretInTheClear { get; } =
    [
        "ClientSecret.Value",
        "ClientSecret!.Value",
        "secret.Value",
        "secret!.Value",
    ];

    public static IReadOnlyList<string> PlaybackTicketHandling { get; } =
    [
        "PlaybackTicket",
        "InTheClear",
    ];

    public static IReadOnlyList<string> Logging { get; } =
    [
        "ILogger",
        "LogTrace",
        "LogDebug",
        "LogInformation",
        "LogWarning",
        "LogError",
        "LogCritical",
        "Console.Write",
    ];

    public static IReadOnlyList<string> OutboundCallers { get; } =
    [
        "HttpClient",
        "IHttpClientFactory",
    ];
}
