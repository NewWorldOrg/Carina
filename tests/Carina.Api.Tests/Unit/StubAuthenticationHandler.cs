using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace Carina.Api.Tests.Unit;

public sealed class StubAuthenticationHandler : IAuthenticationHandler
{
    public Task InitializeAsync(AuthenticationScheme scheme, HttpContext context)
        => Task.CompletedTask;

    public Task<AuthenticateResult> AuthenticateAsync()
        => Task.FromResult(AuthenticateResult.NoResult());

    public Task ChallengeAsync(AuthenticationProperties? properties)
        => Task.CompletedTask;

    public Task ForbidAsync(AuthenticationProperties? properties)
        => Task.CompletedTask;
}
