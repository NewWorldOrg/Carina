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
