using Carina.Domain.Auth;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Unit.Auth;

[Trait("Category", "Unit")]
public sealed class LocalAccountProvisioningDomainServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly HandTurnedClock clock = new(new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero));

    private readonly HeldLocalAccount accounts = new();

    private readonly QuickPasswordHasher hasher = new();

    private LocalAccountProvisioningDomainService Service => new(accounts, hasher, clock);

    [Fact(DisplayName = "BR-AU-004: with no account, one is made under the first name with a password that opens it")]
    public async Task WithNoAccountOneIsMadeWithAPasswordThatOpensIt()
    {
        FirstLocalAccount? made = await Service.FirstAccountAsync(PasswordHashPolicy.Default, Cancel);

        Assert.NotNull(made);
        Assert.Equal(FirstCredentials.Username, made.Account.Username);
        Assert.True(hasher.Matches(made.Password, made.Account.PasswordHash));
        Assert.Equal(clock.GetUtcNow().UtcDateTime, made.Account.CreatedAt);
    }

    [Fact(DisplayName = "BR-AU-004: an account that exists is left alone and nothing is made")]
    public async Task AnAccountThatExistsIsLeftAlone()
    {
        accounts.Account = LocalAccount.Bootstrap(
            "someone",
            hasher.Hash("a password the operator chose", PasswordHashPolicy.Default),
            clock.GetUtcNow().UtcDateTime);

        Assert.Null(await Service.FirstAccountAsync(PasswordHashPolicy.Default, Cancel));
        Assert.Equal("someone", accounts.Account.Username);
    }

    [Fact(DisplayName = "BR-AU-004: the account made is handed back to be saved, and nothing is written here")]
    public async Task TheAccountMadeIsHandedBackRatherThanSaved()
    {
        await Service.FirstAccountAsync(PasswordHashPolicy.Default, Cancel);

        Assert.Null(accounts.Account);
        Assert.Equal(0, accounts.Saves);
    }

    [Fact]
    public async Task ThePasswordMadeDoesNotShowWhereTheAnswerIsPrintedWhole()
    {
        FirstLocalAccount? made = await Service.FirstAccountAsync(PasswordHashPolicy.Default, Cancel);

        Assert.DoesNotContain(made!.Password, made.ToString(), StringComparison.Ordinal);
    }
}
