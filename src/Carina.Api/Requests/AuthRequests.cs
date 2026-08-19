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
