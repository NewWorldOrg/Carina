namespace Carina.Domain.Auth;

public sealed record OidcClaims
{
    public const string GroupsClaim = "groups";

    public required string Issuer { get; init; }

    public required IReadOnlyList<string> Audiences { get; init; }

    public required string Subject { get; init; }

    public required DateTime ExpiresAt { get; init; }

    public string? Nonce { get; init; }

    public IReadOnlyList<string> Groups { get; init; } = [];

    public bool GroupsOverflowed { get; init; }

    public string? HostedDomain { get; init; }
}
