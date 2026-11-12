using Carina.Domain.Auth;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Auth;

public sealed class LocalLoginDomainServiceTests
{
    private const string Password = "a password long enough";

    private const string Caller = "10.0.0.9";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime At = new(2026, 8, 19, 9, 0, 0, DateTimeKind.Utc);

    private readonly HeldLocalAccount accounts = new();

    private readonly CountingPasswordHasher hasher = new(new QuickPasswordHasher());

    private readonly CountedThrottle throttle = new();

    private LocalLoginDomainService Service => new(accounts, hasher, throttle);

    [Fact]
    public async Task TheRightNameAndPasswordOpenTheAccountAndForgetTheCallersTries()
    {
        Seed();

        LocalLogin login = await TryAsync("carina", Password);

        Assert.Same(accounts.Account, login.Admitted);
        Assert.Null(login.HeldOffUntil);
        Assert.Equal([Caller], throttle.Passed);
    }

    [Theory]
    [InlineData("carina", "the wrong password")]
    [InlineData("someone-else", Password)]
    [InlineData("Carina", Password)]
    public async Task AWrongNameOrPasswordOpensNothingAndForgetsNothing(string username, string password)
    {
        Seed();

        LocalLogin login = await TryAsync(username, password);

        Assert.Equal(LocalLogin.Refused, login);
        Assert.Empty(throttle.Passed);
    }

    [Fact]
    public async Task AnEmptyPasswordIsRefusedWithoutAnythingHashedButItsTryIsCounted()
    {
        Seed();
        hasher.Reset();

        LocalLogin login = await TryAsync("carina", string.Empty);

        Assert.Equal(LocalLogin.Refused, login);
        Assert.Equal(0, hasher.Derivations);
        Assert.Equal([Caller], throttle.Taken);
    }

    [Fact]
    public async Task WithNoAccountThePasswordIsStillComparedWithAHashOfTheSameCost()
    {
        Seed();
        hasher.Reset();
        await TryAsync("carina", "the wrong password");
        int forAKnownName = hasher.Derivations;

        accounts.Account = null;
        hasher.Reset();

        LocalLogin login = await TryAsync("carina", Password);

        Assert.Equal(LocalLogin.Refused, login);
        Assert.Equal(forAKnownName, hasher.Derivations);
        Assert.NotEqual(0, hasher.Derivations);
    }

    [Fact]
    public async Task ACallerHeldOffIsAnsweredWithTheMomentItMayTryAgainBeforeAnythingIsCompared()
    {
        Seed();
        hasher.Reset();
        throttle.HoldOffUntil = At.AddMinutes(5);

        LocalLogin login = await TryAsync("carina", Password);

        Assert.Equal(At.AddMinutes(5), login.HeldOffUntil);
        Assert.Null(login.Admitted);
        Assert.Equal(0, hasher.Derivations);
        Assert.Empty(throttle.Passed);
    }

    private void Seed() => accounts.Account = LocalAccount.Bootstrap(
        "carina",
        hasher.Hash(Password, PasswordHashPolicy.Default),
        At);

    private Task<LocalLogin> TryAsync(string username, string password)
        => Service.TryAsync(Caller, username, password, PasswordHashPolicy.Default, Cancel);

    private sealed class CountedThrottle : ILoginThrottle
    {
        public DateTime? HoldOffUntil { get; set; }

        public List<string> Taken { get; } = [];

        public List<string> Passed { get; } = [];

        public DateTime? TakeTry(string key)
        {
            Taken.Add(key);

            return HoldOffUntil;
        }

        void ILoginThrottle.Passed(string key) => Passed.Add(key);
    }
}
