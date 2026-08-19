namespace Carina.Api.Requests;

public sealed record LoginRequest
{
    public string? Username { get; init; }

    public string? Password { get; init; }
}

public sealed record ChangePasswordRequest
{
    public string? CurrentPassword { get; init; }

    public string? NewPassword { get; init; }
}

public sealed record OidcConfigRequest
{
    public string? DiscoveryUrl { get; init; }

    public string? ClientId { get; init; }

    public string? ClientSecret { get; init; }

    public IReadOnlyList<string>? AllowedGroups { get; init; }

    public IReadOnlyList<string>? AllowedHostedDomains { get; init; }
}
