using Carina.Api.Common;
using Carina.Api.Services;
using Carina.BroadcastTestSupport;
using Carina.Domain.Auth;
using Carina.Infrastructure.Auth;
using Carina.TestSupport;

namespace Carina.Api.Tests.Unit;

public sealed class LocalAccountServiceTests
{
    private const string Password = "a password long enough";

    private const string Caller = "10.0.0.9";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly HeldClock clock = new(new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero));

    private readonly HeldLocalAccount accounts = new();

    private readonly HeldAuthSessions sessions = new();

    private readonly CountingPasswordHasher hasher = new(new QuickPasswordHasher());

    private LocalAccountService? held;

    [Fact]
    public async Task TheRightUsernameAndPasswordStartASessionForThatAccount()
    {
        Seed();

        LoginOutcome outcome = await LogInAsync(FirstCredentials.Username, Password);

        Assert.NotNull(outcome.Session);
        Assert.Equal(FirstCredentials.Username, outcome.Session.Subject.Value);
        Assert.Equal(FirstCredentials.Username, outcome.Session.DisplayName);
        Assert.Equal(AuthMethod.Local, outcome.Session.Method);
        Assert.Single(sessions.Sessions);
    }

    [Fact]
    public async Task AWrongPasswordStartsNothing()
    {
        Seed();

        LoginOutcome outcome = await LogInAsync(FirstCredentials.Username, "the wrong password");

        Assert.Null(outcome.Session);
        Assert.Empty(sessions.Sessions);
    }

    [Fact]
    public async Task AUsernameThatIsNotTheAccountsIsRefusedEvenWithTheRightPassword()
    {
        Seed();

        LoginOutcome outcome = await LogInAsync("someone-else", Password);

        Assert.Null(outcome.Session);
    }

    [Fact]
    public async Task AnUnknownUsernameCostsTheSameKeyDerivationAsAKnownOneSoTheTimingTellsNothing()
    {
        Seed();
        hasher.Reset();

        await LogInAsync(FirstCredentials.Username, "the wrong password");
        int forAKnownName = hasher.Derivations;

        hasher.Reset();
        accounts.Account = null;

        await LogInAsync("nobody-by-that-name", "the wrong password");

        Assert.Equal(forAKnownName, hasher.Derivations);
        Assert.NotEqual(0, hasher.Derivations);
    }

    [Fact]
    public async Task AnEmptyPasswordIsRefusedRatherThanHandedToTheHasher()
    {
        Seed();
        hasher.Reset();

        LoginOutcome outcome = await LogInAsync(FirstCredentials.Username, string.Empty);

        Assert.Null(outcome.Session);
        Assert.Equal(0, hasher.Derivations);
    }

    [Fact]
    public async Task TheCallerIsHeldOffOnceItHasSpentThePolicysWrongTries()
    {
        Seed();

        for (int attempt = 0; attempt < LoginRatePolicy.Default.FailuresBeforeRefusing; attempt++)
        {
            Assert.Null((await LogInAsync(FirstCredentials.Username, "the wrong password")).RetryAt);
        }

        LoginOutcome outcome = await LogInAsync(FirstCredentials.Username, Password);

        Assert.Null(outcome.Session);
        Assert.Equal(clock.GetUtcNow().UtcDateTime + LoginRatePolicy.Default.Window, outcome.RetryAt);
    }

    [Fact]
    public async Task ARightPasswordBeforeTheLimitClearsWhatTheWrongOnesBuiltUp()
    {
        Seed();

        for (int attempt = 0; attempt < LoginRatePolicy.Default.FailuresBeforeRefusing - 1; attempt++)
        {
            await LogInAsync(FirstCredentials.Username, "the wrong password");
        }

        await LogInAsync(FirstCredentials.Username, Password);

        for (int attempt = 0; attempt < LoginRatePolicy.Default.FailuresBeforeRefusing - 1; attempt++)
        {
            Assert.Null((await LogInAsync(FirstCredentials.Username, "the wrong password")).RetryAt);
        }
    }

    [Fact]
    public async Task ChangingThePasswordEndsEveryOtherDeviceAndLeavesTheOneThatAskedSignedIn()
    {
        Seed();

        AuthSession here = await StartedAsync("this device");
        AuthSession there = await StartedAsync("another device");
        AuthSession elsewhere = await StartedAsync("a third device");

        ServiceResult<int, PasswordRefusal> asked = await ChangeAsync(here, Password, "a replacement password");

        Assert.True(asked.IsSuccess);
        Assert.Equal(2, asked.Data);
        Assert.Equal(SessionStatus.Active, here.StatusAt(Now(), SessionPolicy.Default));
        Assert.Equal(SessionStatus.Revoked, there.StatusAt(Now(), SessionPolicy.Default));
        Assert.Equal(SessionStatus.Revoked, elsewhere.StatusAt(Now(), SessionPolicy.Default));
    }

    [Fact]
    public async Task ThePasswordThatWasSetIsTheOneThatOpensTheAccountAfterwards()
    {
        Seed();

        AuthSession here = await StartedAsync("this device");

        await ChangeAsync(here, Password, "a replacement password");

        Assert.Null((await LogInAsync(FirstCredentials.Username, Password)).Session);
        Assert.NotNull((await LogInAsync(FirstCredentials.Username, "a replacement password")).Session);
    }

    [Fact]
    public async Task TheWrongCurrentPasswordChangesNothingAndEndsNothing()
    {
        Seed();

        AuthSession here = await StartedAsync("this device");
        AuthSession there = await StartedAsync("another device");
        string held = accounts.Account!.PasswordHash.Value;

        ServiceResult<int, PasswordRefusal> asked = await ChangeAsync(here, "not the password", "a replacement password");

        Assert.False(asked.IsSuccess);
        Assert.Equal(PasswordRefusal.WrongPassword, asked.ErrorType);
        Assert.Equal(held, accounts.Account.PasswordHash.Value);
        Assert.Equal(SessionStatus.Active, there.StatusAt(Now(), SessionPolicy.Default));
    }

    [Fact]
    public async Task AReplacementTooShortToBeWorthHashingIsRefusedAndEndsNothing()
    {
        Seed();

        AuthSession here = await StartedAsync("this device");
        AuthSession there = await StartedAsync("another device");

        ServiceResult<int, PasswordRefusal> asked = await ChangeAsync(
            here,
            Password,
            new string('x', LocalAccountService.ShortestPassword - 1));

        Assert.False(asked.IsSuccess);
        Assert.Equal(PasswordRefusal.TooWeak, asked.ErrorType);
        Assert.Equal(SessionStatus.Active, there.StatusAt(Now(), SessionPolicy.Default));
    }

    private LocalAccountService Held => held ??= Service();

    private LocalAccountService Service() => new(
        accounts,
        sessions,
        hasher,
        new LoginThrottle(LoginRatePolicy.Default, clock),
        PasswordHashPolicy.Default,
        SessionPolicy.Default,
        clock);

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private void Seed() => accounts.Account = LocalAccount.Bootstrap(
        FirstCredentials.Username,
        hasher.Hash(Password, PasswordHashPolicy.Default),
        Now());

    private async Task<LoginOutcome> LogInAsync(string username, string password)
    {
        ServiceResult<LoginOutcome> asked = await Held.LogInAsync(
            new LoginAttempt(username, password, "a device", Caller),
            Cancel);

        return asked.Data!;
    }

    private async Task<AuthSession> StartedAsync(string device)
    {
        ServiceResult<LoginOutcome> asked = await Held.LogInAsync(
            new LoginAttempt(FirstCredentials.Username, Password, device, Caller),
            Cancel);

        return asked.Data!.Session!;
    }

    private Task<ServiceResult<int, PasswordRefusal>> ChangeAsync(
        AuthSession here,
        string current,
        string replacement)
        => Held.ChangePasswordAsync(
            new PasswordChange(here.Subject, here.Id, current, replacement),
            Cancel);
}
