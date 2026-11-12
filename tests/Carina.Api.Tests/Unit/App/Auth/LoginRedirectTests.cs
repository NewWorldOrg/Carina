using Carina.Api.Authentication;

namespace Carina.Api.Tests.Unit;

public sealed class LoginRedirectTests
{
    [Theory]
    [InlineData("/guide", "/guide")]
    [InlineData("/programs?type=terrestrial&from=now", "/programs?type=terrestrial&from=now")]
    [InlineData("/settings/tuners", "/settings/tuners")]
    public void AHostRelativePathIsKept(string target, string kept)
    {
        Assert.Equal(kept, LoginRedirect.Within(target));
    }

    [Theory]
    [InlineData("https://elsewhere.example/guide")]
    [InlineData("http://elsewhere.example")]
    [InlineData("//elsewhere.example/guide")]
    [InlineData("/\\elsewhere.example")]
    [InlineData("/guide\\..\\elsewhere")]
    [InlineData("guide")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingThatCouldLeaveThisHostFallsBackHome(string? target)
    {
        Assert.Equal(LoginRedirect.Home, LoginRedirect.Within(target));
    }

    [Theory]
    [InlineData("/guide\nLocation: https://elsewhere.example")]
    [InlineData("/guide\rSet-Cookie: taken=1")]
    public void ATargetCarryingHeaderBreaksFallsBackHome(string target)
    {
        Assert.Equal(LoginRedirect.Home, LoginRedirect.Within(target));
    }

    [Theory(DisplayName = "BR-AU-003: a letter outside ASCII, or a space, is kept escaped so the target can stand in a response header")]
    [InlineData("/search?q=ニュース", "/search?q=%E3%83%8B%E3%83%A5%E3%83%BC%E3%82%B9")]
    [InlineData("/search?q=a b", "/search?q=a%20b")]
    [InlineData("/library?q=café", "/library?q=caf%C3%A9")]
    [InlineData("/search?q=📺", "/search?q=%F0%9F%93%BA")]
    [InlineData("/search?q=%E3%83%8B", "/search?q=%E3%83%8B")]
    public void BrAu003ALetterOutsideAsciiIsKeptEscaped(string target, string kept)
    {
        Assert.Equal(kept, LoginRedirect.Within(target));
    }

    [Fact(DisplayName = "BR-AU-003: a half of a surrogate pair standing alone is escaped as the replacement character rather than passed on")]
    public void BrAu003AHalfOfASurrogatePairStandingAloneIsEscapedAsTheReplacementCharacter()
    {
        Assert.Equal("/search?q=%EF%BF%BD", LoginRedirect.Within("/search?q=\uD83D"));
    }

    [Fact(DisplayName = "BR-AU-003: an escaped target is carried to the login screen escaped once more as the value of next")]
    public void BrAu003AnEscapedTargetIsCarriedToTheLoginScreen()
    {
        Assert.Equal("/login?next=%2Fsearch%3Fq%3Da%2520b", LoginRedirect.For("/search?q=a b"));
    }

    [Theory]
    [InlineData("/login")]
    [InlineData("/login?next=%2Fguide")]
    [InlineData("/LOGIN")]
    public void TheLoginScreenIsNotItsOwnReturnTarget(string target)
    {
        Assert.Equal(LoginRedirect.Home, LoginRedirect.Within(target));
    }

    [Fact]
    public void TheRedirectCarriesTheEncodedReturnTarget()
    {
        Assert.Equal("/login?next=%2Fprograms%3Ftype%3Dterrestrial", LoginRedirect.For("/programs?type=terrestrial"));
    }

    [Fact]
    public void TheRedirectOfARefusedTargetPointsHome()
    {
        Assert.Equal("/login?next=%2F", LoginRedirect.For("https://elsewhere.example/guide"));
    }
}
