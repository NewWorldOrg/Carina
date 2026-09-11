using Carina.Domain.Auth;
using Carina.Infrastructure.Auth;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Auth;

public sealed class AuthUpkeepJobTests
{
    private static readonly DateTime At = new(2026, 8, 19, 9, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task ASessionSomebodyIsStillSignedInWithIsLeftWhereItIs()
    {
        var held = new HeldAuthSessions();
        AuthSession asking = Sitting(held, "the device still asking", At);

        using AuthUpkeepJob job = Upkeep(At + SessionPolicy.Default.IdleTimeout - TimeSpan.FromHours(1));

        Assert.Equal(0, await job.ForgetEndedSessionsAsync(held, Cancel));
        Assert.Equal(asking.Id, Assert.Single(held.Sessions).Id);
    }

    [Fact]
    public async Task ASessionNobodyCanSignInWithAnyMoreIsForgotten()
    {
        var held = new HeldAuthSessions();
        Sitting(held, "a device that stopped asking", At);

        using AuthUpkeepJob job = Upkeep(At + SessionPolicy.Default.IdleTimeout);

        Assert.Equal(1, await job.ForgetEndedSessionsAsync(held, Cancel));
        Assert.Empty(held.Sessions);
    }

    [Fact]
    public async Task ASessionEndedAMomentAgoIsKeptAsLongAsAnUnusedOneWouldHaveBeen()
    {
        var held = new HeldAuthSessions();

        Sitting(held, "a device signed out from elsewhere", At).Revoke(At.AddMinutes(1));

        using AuthUpkeepJob job = Upkeep(At.AddMinutes(2));

        Assert.Equal(0, await job.ForgetEndedSessionsAsync(held, Cancel));
        Assert.Single(held.Sessions);
    }

    [Fact]
    public async Task ASessionEndedLongEnoughAgoIsForgottenToo()
    {
        var held = new HeldAuthSessions();

        Sitting(held, "a device signed out from elsewhere", At).Revoke(At.AddMinutes(1));

        using AuthUpkeepJob job = Upkeep(At.AddMinutes(1) + SessionPolicy.Default.IdleTimeout);

        Assert.Equal(1, await job.ForgetEndedSessionsAsync(held, Cancel));
        Assert.Empty(held.Sessions);
    }

    [Fact]
    public async Task ARoundLeavesTheOnesInUseBehindAndTakesOnlyTheRest()
    {
        var held = new HeldAuthSessions();
        Sitting(held, "a device that stopped asking", At);
        AuthSession asking = Sitting(held, "the device still asking", At.AddDays(6));

        using AuthUpkeepJob job = Upkeep(At + SessionPolicy.Default.IdleTimeout);

        Assert.Equal(1, await job.ForgetEndedSessionsAsync(held, Cancel));
        Assert.Equal(asking.Id, Assert.Single(held.Sessions).Id);
    }

    [Fact]
    public async Task ARoundOfUpkeepIsWhatReachesTheSessionsRatherThanAnythingAskingForIt()
    {
        var held = new HeldAuthSessions();
        Sitting(held, "a device that stopped asking", At);

        await using ServiceProvider serving = new ServiceCollection()
            .AddSingleton<IAuthSessionRepository>(held)
            .AddSingleton<IOidcSettingsRepository>(new HeldOidcSettings())
            .AddSingleton<IOidcDirectory, NoDirectory>()
            .BuildServiceProvider();

        using var job = new AuthUpkeepJob(
            serving.GetRequiredService<IServiceScopeFactory>(),
            new OidcReachability(),
            SessionPolicy.Default,
            new WoundClock(At + SessionPolicy.Default.IdleTimeout),
            NullLogger<AuthUpkeepJob>.Instance);

        await job.UpkeepOnceAsync(Cancel);

        Assert.Empty(held.Sessions);
    }

    private static AuthSession Sitting(HeldAuthSessions held, string device, DateTime at)
    {
        AuthSession session = AuthSession.Start(
            SessionId.Issue(),
            new Subject("carina"),
            "carina",
            AuthMethod.Local,
            device,
            at);

        held.Sessions.Add(session);

        return session;
    }

    private static AuthUpkeepJob Upkeep(DateTime now)
        => new(
            new ThrowingScopes(),
            new OidcReachability(),
            SessionPolicy.Default,
            new WoundClock(now),
            NullLogger<AuthUpkeepJob>.Instance);

    private sealed class ThrowingScopes : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new InvalidOperationException("Nothing here asks for a scope.");
    }

    private sealed class NoDirectory : IOidcDirectory
    {
        public Task<OidcEndpoints?> ForAsync(OidcSettings settings, CancellationToken cancellationToken)
            => Task.FromResult<OidcEndpoints?>(null);

        public Task<OidcEndpoints?> ProbeAsync(OidcSettings settings, CancellationToken cancellationToken)
            => Task.FromResult<OidcEndpoints?>(null);
    }
}
