using Carina.Domain.Auth;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Auth;

public sealed class PasswordChangeDomainServiceTests
{
    private const string Password = "a password long enough";

    private const string Replacement = "a replacement password";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTimeOffset Start = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    private readonly HandTurnedClock clock = new(Start);

    private readonly HeldLocalAccount accounts = new();

    private readonly HeldAuthSessions sessions = new();

    private readonly CountingPasswordHasher hasher = new(new QuickPasswordHasher());

    private PasswordChangeDomainService Service => new(accounts, sessions, hasher, clock);

    [Fact(DisplayName = "BR-AU-005: changing the password ends every session of the one who changed it except the one that asked")]
    public async Task ChangingThePasswordEndsEveryOtherSessionOfTheOneWhoChangedIt()
    {
        Seed();
        AuthSession here = Session("this device", "carina");
        AuthSession there = Session("another device", "carina");
        AuthSession someoneElse = Session("their device", "someone-else");
        clock.Turn(TimeSpan.FromMinutes(1));

        PasswordChangeOutcome outcome = await ChangeAsync(here, Password, Replacement);

        Assert.Null(outcome.Refusal);
        Assert.Equal([there], outcome.Ended);
        Assert.Equal(Now(), there.RevokedAt);
        Assert.Null(here.RevokedAt);
        Assert.Null(someoneElse.RevokedAt);
    }

    [Fact(DisplayName = "BR-AU-005: a session already revoked is not ended a second time")]
    public async Task ASessionAlreadyRevokedIsNotEndedASecondTime()
    {
        Seed();
        AuthSession here = Session("this device", "carina");
        AuthSession gone = Session("another device", "carina");
        gone.Revoke(Now());
        clock.Turn(TimeSpan.FromMinutes(1));

        PasswordChangeOutcome outcome = await ChangeAsync(here, Password, Replacement);

        Assert.Empty(outcome.Ended);
        Assert.Equal(Start.UtcDateTime, gone.RevokedAt);
    }

    [Fact(DisplayName = "BR-AU-022: the changed account holds the new password from the moment it was changed, and nothing is written here")]
    public async Task TheChangedAccountHoldsTheNewPasswordAndNothingIsWrittenHere()
    {
        Seed();
        AuthSession here = Session("this device", "carina");
        clock.Turn(TimeSpan.FromMinutes(1));

        PasswordChangeOutcome outcome = await ChangeAsync(here, Password, Replacement);

        Assert.Same(accounts.Account, outcome.Changed);
        Assert.True(hasher.Matches(Replacement, outcome.Changed!.PasswordHash));
        Assert.Equal(Now(), outcome.Changed.PasswordChangedAt);
        Assert.Equal(0, accounts.Saves);
    }

    [Fact(DisplayName = "BR-AU-022: with no account there is no current password to be right")]
    public async Task WithNoAccountTheCurrentPasswordIsWrong()
    {
        AuthSession here = Session("this device", "carina");

        PasswordChangeOutcome outcome = await ChangeAsync(here, Password, Replacement);

        Assert.Equal(PasswordChangeRefusal.WrongPassword, outcome.Refusal);
        Assert.Null(outcome.Changed);
    }

    [Theory(DisplayName = "BR-AU-022: a refused change leaves the password and every session as they were")]
    [InlineData("not the password", Replacement, PasswordChangeRefusal.WrongPassword)]
    [InlineData("", Replacement, PasswordChangeRefusal.WrongPassword)]
    [InlineData(Password, "too short", PasswordChangeRefusal.OutOfLength)]
    public async Task ARefusedChangeLeavesThePasswordAndEverySessionAsTheyWere(
        string current,
        string replacement,
        PasswordChangeRefusal refusal)
    {
        Seed();
        AuthSession here = Session("this device", "carina");
        AuthSession there = Session("another device", "carina");
        string held = accounts.Account!.PasswordHash.Value;

        PasswordChangeOutcome outcome = await ChangeAsync(here, current, replacement);

        Assert.Equal(refusal, outcome.Refusal);
        Assert.Equal(held, accounts.Account.PasswordHash.Value);
        Assert.Null(there.RevokedAt);
        Assert.Empty(outcome.Ended);
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private void Seed() => accounts.Account = LocalAccount.Bootstrap(
        "carina",
        hasher.Hash(Password, PasswordHashPolicy.Default),
        Now());

    private AuthSession Session(string device, string subject)
    {
        AuthSession session = AuthSession.Start(
            SessionId.Issue(),
            new Subject(subject),
            subject,
            AuthMethod.Local,
            device,
            Now());

        sessions.Sessions.Add(session);

        return session;
    }

    private Task<PasswordChangeOutcome> ChangeAsync(AuthSession here, string current, string replacement)
        => Service.ChangeAsync(here.Subject, here.Handle, current, replacement, PasswordHashPolicy.Default, Cancel);
}
