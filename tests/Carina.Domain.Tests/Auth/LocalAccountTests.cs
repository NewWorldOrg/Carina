using Carina.Domain.Auth;

namespace Carina.Domain.Tests.Auth;

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

    private static LocalAccount Account() => LocalAccount.Bootstrap("carina", Hash(0x11), Created);

    private static PasswordHash Hash(byte fill)
        => PasswordHash.Encode(
            PasswordHashPolicy.Default,
            new byte[PasswordHashPolicy.Default.SaltLength],
            [.. Enumerable.Repeat(fill, PasswordHashPolicy.Default.DigestLength)]);
}
