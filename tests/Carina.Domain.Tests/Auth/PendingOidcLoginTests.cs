using Carina.Domain.Auth;

namespace Carina.Domain.Tests.Auth;

public sealed class PendingOidcLoginTests
{
    private static readonly DateTime At = new(2026, 8, 19, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EveryHandshakeCarriesItsOwnStateNonceAndVerifier()
    {
        PendingOidcLogin first = Begun();
        PendingOidcLogin second = Begun();

        Assert.NotEqual(first.State, second.State);
        Assert.NotEqual(first.Nonce, second.Nonce);
        Assert.NotEqual(first.Pkce.Verifier, second.Pkce.Verifier);
    }

    [Fact]
    public void AHandshakeRemembersWhereTheCallerWasHeaded()
    {
        Assert.Equal("/settings/authentication", Begun("/settings/authentication").ReturnPath);
    }

    [Theory]
    [InlineData("https://elsewhere.example/")]
    [InlineData("settings")]
    [InlineData("")]
    public void AHandshakeWillNotCarryACallerAnywhereButInsideThisHost(string returnPath)
    {
        Assert.Throws<ArgumentException>(
            () => PendingOidcLogin.Begin(Unguessable.Issue(), returnPath, At));
    }

    [Fact]
    public void AHandshakeBelongsToTheBrowserThatStartedIt()
    {
        string mark = Unguessable.Issue();
        PendingOidcLogin pending = PendingOidcLogin.Begin(mark, "/", At);

        Assert.True(pending.BelongsTo(mark));
        Assert.False(pending.BelongsTo(Unguessable.Issue()));
        Assert.False(pending.BelongsTo(null));
    }

    [Fact]
    public void AHandshakeLeftOpenPastItsWindowIsNoLongerOneToFinish()
    {
        PendingOidcLogin pending = Begun();
        TimeSpan window = OidcLoginPolicy.Default.HandshakeLifetime;

        Assert.False(pending.HasLapsed(At + window - TimeSpan.FromSeconds(1), OidcLoginPolicy.Default));
        Assert.True(pending.HasLapsed(At + window, OidcLoginPolicy.Default));
    }

    [Fact]
    public void AHandshakeIsMarkedByABrowserAndNothingWeakerWillDo()
    {
        Assert.Throws<ArgumentException>(() => PendingOidcLogin.Begin("guessable", "/", At));
    }

    private static PendingOidcLogin Begun(string returnPath = "/")
        => PendingOidcLogin.Begin(Unguessable.Issue(), returnPath, At);
}
