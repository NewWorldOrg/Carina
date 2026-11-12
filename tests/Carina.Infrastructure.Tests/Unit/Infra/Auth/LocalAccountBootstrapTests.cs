using Carina.BroadcastTestSupport;
using Carina.Domain.Auth;
using Carina.Infrastructure.Auth;
using Carina.Infrastructure.Tests.Auth;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Tests.Unit.Infra.Auth;

[Trait("Category", "Unit")]
public sealed class LocalAccountBootstrapTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly HeldClock clock = new(new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero));

    private readonly HeldLocalAccount accounts = new();

    private readonly QuickPasswordHasher hasher = new();

    private readonly RecordedLog log = new();

    [Fact]
    public async Task AnAppThatStartsWithNoCredentialsMakesSomeRatherThanWaitOpen()
    {
        await Bootstrap().EnsureAnAccountExistsAsync(Provisioning(), accounts, Cancel);

        Assert.NotNull(accounts.Account);
        Assert.Equal(FirstCredentials.Username, accounts.Account.Username);
    }

    [Fact]
    public async Task ThePasswordIsWrittenOutOnceAndItIsTheOneThatOpensTheAccount()
    {
        await Bootstrap().EnsureAnAccountExistsAsync(Provisioning(), accounts, Cancel);

        string written = log.Only("Password");

        Assert.Single(log.Lines);
        Assert.Contains(FirstCredentials.Username, log.Lines[0], StringComparison.Ordinal);
        Assert.True(hasher.Matches(written, accounts.Account!.PasswordHash));
    }

    [Fact]
    public async Task ASecondStartLeavesTheAccountAloneAndSaysNothingMore()
    {
        LocalAccountBootstrap bootstrap = Bootstrap();

        await bootstrap.EnsureAnAccountExistsAsync(Provisioning(), accounts, Cancel);

        string first = accounts.Account!.PasswordHash.Value;
        int saves = accounts.Saves;

        clock.MoveOn(TimeSpan.FromDays(1));

        await bootstrap.EnsureAnAccountExistsAsync(Provisioning(), accounts, Cancel);

        Assert.Equal(first, accounts.Account!.PasswordHash.Value);
        Assert.Equal(saves, accounts.Saves);
        Assert.Single(log.Lines);
    }

    [Fact]
    public async Task AnAccountThatSomeoneAlreadyChangedIsNeverReplacedBySomethingWrittenToALog()
    {
        accounts.Account = LocalAccount.Bootstrap(
            "someone",
            hasher.Hash("a password the operator chose", PasswordHashPolicy.Default),
            clock.GetUtcNow().UtcDateTime);

        await Bootstrap().EnsureAnAccountExistsAsync(Provisioning(), accounts, Cancel);

        Assert.Equal("someone", accounts.Account.Username);
        Assert.Empty(log.Lines);
        Assert.Equal(0, accounts.Saves);
    }

    [Fact]
    public async Task AStartThatCannotReachTheStoreSaysSoRatherThanBringingTheAppDown()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILocalAccountRepository>(new RefusingAccounts());
        services.AddSingleton<IPasswordHasher>(hasher);
        services.AddSingleton<TimeProvider>(clock);
        services.AddScoped<LocalAccountProvisioningDomainService>();

        await using ServiceProvider provider = services.BuildServiceProvider();

        var bootstrap = new LocalAccountBootstrap(
            provider.GetRequiredService<IServiceScopeFactory>(),
            PasswordHashPolicy.Default,
            log);

        await bootstrap.StartAsync(Cancel);
        await bootstrap.ExecuteTask!;
        await bootstrap.StopAsync(Cancel);

        Assert.Single(log.Lines);
        Assert.Contains("nobody can sign in", log.Lines[0], StringComparison.Ordinal);
        Assert.Contains("The store is not there.", log.Failures, StringComparer.Ordinal);
    }

    private LocalAccountBootstrap Bootstrap()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILocalAccountRepository>(accounts);

        return new LocalAccountBootstrap(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            PasswordHashPolicy.Default,
            log);
    }

    private LocalAccountProvisioningDomainService Provisioning() => new(accounts, hasher, clock);
}

internal sealed class RefusingAccounts : ILocalAccountRepository
{
    public Task<LocalAccount?> FindAsync(CancellationToken cancellationToken)
        => throw new InvalidOperationException("The store is not there.");

    public Task SaveAsync(LocalAccount account, CancellationToken cancellationToken)
        => throw new InvalidOperationException("The store is not there.");
}
