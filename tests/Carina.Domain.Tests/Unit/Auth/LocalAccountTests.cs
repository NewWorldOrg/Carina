using Carina.Domain.Auth;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Unit.Auth;

[Trait("Category", "Unit")]
public sealed class LocalAccountTests
{
    private static readonly DateTime Created = new(2026, 8, 19, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TheBootstrappedAccountIsTheOnlyRowThereEverIs()
    {
        LocalAccount account = Account();

        Assert.Equal(LocalAccount.TheOnlyRow, account.Id);
    }

    [Fact]
    public void ABootstrappedAccountStartsWithItsPasswordAsOldAsItself()
    {
        LocalAccount account = Account();

        Assert.Equal(Created, account.CreatedAt);
        Assert.Equal(Created, account.PasswordChangedAt);
    }

    [Fact]
    public void ChangingThePasswordMovesTheMomentOtherSessionsAreMeasuredAgainst()
    {
        LocalAccount account = Account();

        account.ChangePassword(Hash(0x22), Created.AddDays(3));

        Assert.Equal(Created.AddDays(3), account.PasswordChangedAt);
        Assert.Equal(Created, account.CreatedAt);
    }

    [Fact]
    public void ChangingThePasswordReplacesTheStoredHash()
    {
        LocalAccount account = Account();

        account.ChangePassword(Hash(0x22), Created.AddDays(3));

        Assert.Equal(Hash(0x22), account.PasswordHash);
    }

    [Fact]
    public void RemakingTheHashOfTheSamePasswordReplacesWhatIsStored()
    {
        LocalAccount account = Account();

        account.ReplaceTheHashOfTheSamePassword(Hash(0x22));

        Assert.Equal(Hash(0x22), account.PasswordHash);
    }

    [Fact]
    public void RemakingTheHashOfTheSamePasswordLeavesTheMomentOtherSessionsAreMeasuredAgainstWhereItWas()
    {
        LocalAccount account = Account();

        account.ChangePassword(Hash(0x22), Created.AddDays(3));
        account.ReplaceTheHashOfTheSamePassword(Hash(0x33));

        Assert.Equal(Created.AddDays(3), account.PasswordChangedAt);
        Assert.Equal(Created, account.CreatedAt);
    }

    [Fact]
    public void RemakingTheHashNeedsAHashToRemakeItTo()
    {
        LocalAccount account = Account();

        Assert.Throws<ArgumentNullException>(() => account.ReplaceTheHashOfTheSamePassword(null!));
    }

    [Fact]
    public void APasswordCannotHaveBeenChangedBeforeTheAccountExisted()
    {
        LocalAccount account = Account();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => account.ChangePassword(Hash(0x22), Created.AddSeconds(-1)));
    }

    [Fact]
    public void ChangingThePasswordNeedsAHashToChangeItTo()
    {
        LocalAccount account = Account();

        Assert.Throws<ArgumentNullException>(() => account.ChangePassword(null!, Created.AddDays(1)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnAccountWithoutAUsernameCannotBeSignedInTo(string username)
    {
        Assert.Throws<ArgumentException>(() => LocalAccount.Bootstrap(username, Hash(0x11), Created));
    }

    [Fact]
    public void AUsernameIsTrimmedRatherThanStoredWithItsPadding()
    {
        LocalAccount account = LocalAccount.Bootstrap("  carina  ", Hash(0x11), Created);

        Assert.Equal("carina", account.Username);
    }

    [Fact]
    public void AUsernameLongerThanTheColumnIsRefusedBeforeTheDatabaseSeesIt()
    {
        string tooLong = new('u', LocalAccount.LongestUsername + 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => LocalAccount.Bootstrap(tooLong, Hash(0x11), Created));
    }

    [Fact]
    public void AUsernameCarryingWhitespaceInsideItWouldNotSurviveBeingTypedBack()
    {
        Assert.Throws<ArgumentException>(() => LocalAccount.Bootstrap("car ina", Hash(0x11), Created));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void AnAccountRefusesATimeThatIsNotUtc(DateTimeKind kind)
    {
        Assert.Throws<ArgumentException>(
            () => LocalAccount.Bootstrap("carina", Hash(0x11), DateTime.SpecifyKind(Created, kind)));
    }

    [Fact]
    public void ARehydratedAccountCarriesBackEverythingTheRowHeld()
    {
        LocalAccount account = LocalAccount.Rehydrate(
            LocalAccount.TheOnlyRow,
            "carina",
            Hash(0x11),
            Created,
            Created.AddDays(9));

        Assert.Equal("carina", account.Username);
        Assert.Equal(Created.AddDays(9), account.PasswordChangedAt);
    }

    [Fact]
    public void ARowClaimingItsPasswordChangedBeforeItExistedIsNotAnAccount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LocalAccount.Rehydrate(
                LocalAccount.TheOnlyRow,
                "carina",
                Hash(0x11),
                Created,
                Created.AddSeconds(-1)));
    }

    [Fact(DisplayName = "BR-AU-022: an empty password is never worth comparing with the stored hash")]
    public void AnEmptyPasswordIsNotAPassword()
    {
        Assert.False(LocalAccount.IsAPassword(null));
        Assert.False(LocalAccount.IsAPassword(string.Empty));
        Assert.True(LocalAccount.IsAPassword(" "));
    }

    [Theory(DisplayName = "BR-AU-022: a new password is between the shortest and the longest length, both included")]
    [InlineData(LocalAccount.ShortestPassword - 1, false)]
    [InlineData(LocalAccount.ShortestPassword, true)]
    [InlineData(LocalAccount.LongestPassword, true)]
    [InlineData(LocalAccount.LongestPassword + 1, false)]
    public void ANewPasswordFitsBetweenTheShortestAndTheLongest(int length, bool fits)
    {
        Assert.Equal(fits, LocalAccount.FitsAsAPassword(new string('x', length)));
    }

    [Fact(DisplayName = "BR-AU-022: the password is not changed from a current one that is not this account's")]
    public void ThePasswordIsNotChangedFromTheWrongCurrentOne()
    {
        var hasher = new QuickPasswordHasher();
        LocalAccount account = Holding(hasher, "a password long enough");

        Assert.Equal(
            PasswordChangeRefusal.WrongPassword,
            account.RefusesToChange("not the password", "a replacement password", hasher));
    }

    [Fact(DisplayName = "BR-AU-022: an empty current password is refused without anything hashed")]
    public void AnEmptyCurrentPasswordIsRefusedWithoutAnythingHashed()
    {
        var hasher = new CountingPasswordHasher(new QuickPasswordHasher());
        LocalAccount account = Holding(hasher, "a password long enough");
        hasher.Reset();

        Assert.Equal(PasswordChangeRefusal.WrongPassword, account.RefusesToChange(string.Empty, "a replacement password", hasher));
        Assert.Equal(0, hasher.Derivations);
    }

    [Fact(DisplayName = "BR-AU-022: the current password is checked before the length of the new one")]
    public void TheCurrentPasswordIsCheckedBeforeTheLengthOfTheNewOne()
    {
        var hasher = new QuickPasswordHasher();
        LocalAccount account = Holding(hasher, "a password long enough");

        Assert.Equal(PasswordChangeRefusal.WrongPassword, account.RefusesToChange("not the password", "short", hasher));
        Assert.Equal(PasswordChangeRefusal.OutOfLength, account.RefusesToChange("a password long enough", "short", hasher));
        Assert.Null(account.RefusesToChange("a password long enough", "a replacement password", hasher));
    }

    [Fact]
    public void TheAccountIsSignedInToByItsNameExactly()
    {
        LocalAccount account = Account();

        Assert.True(account.IsSignedInToAs("carina"));
        Assert.False(account.IsSignedInToAs("Carina"));
        Assert.False(account.IsSignedInToAs("carina "));
    }

    private static LocalAccount Holding(IPasswordHasher hasher, string password)
        => LocalAccount.Bootstrap("carina", hasher.Hash(password, PasswordHashPolicy.Default), Created);

    private static LocalAccount Account() => LocalAccount.Bootstrap("carina", Hash(0x11), Created);

    private static PasswordHash Hash(byte fill)
        => PasswordHash.Encode(
            PasswordHashPolicy.Default,
            new byte[PasswordHashPolicy.Default.SaltLength],
            [.. Enumerable.Repeat(fill, PasswordHashPolicy.Default.DigestLength)]);
}
