using Carina.Domain.Notifications;

namespace Carina.Domain.Tests.Notifications;

public sealed class NoticeLinkBaseTests
{
    [Fact(DisplayName = "a link is the path of the screen on the origin the screens are reached at")]
    public void ALinkIsThePathOnTheOrigin()
    {
        NoticeLinkBase links = NoticeLinkBase.At(new Uri("https://screens.example"));

        Assert.True(links.IsKnown);
        Assert.Equal("https://screens.example/settings/quality", links.To("/settings/quality"));
    }

    [Fact(DisplayName = "with no origin known, no link is made")]
    public void WithNoOriginKnownNoLinkIsMade()
    {
        Assert.False(NoticeLinkBase.None.IsKnown);
        Assert.Null(NoticeLinkBase.None.To("/settings/quality"));
    }

    [Theory(DisplayName = "the origin is written as the scheme, the host and a port that is not the usual one, with no slash after them")]
    [InlineData("https://screens.example", "https://screens.example")]
    [InlineData("https://screens.example/", "https://screens.example")]
    [InlineData("https://screens.example:443", "https://screens.example")]
    [InlineData("https://screens.example:8443/", "https://screens.example:8443")]
    [InlineData("http://screens.example", "http://screens.example")]
    public void TheOriginIsWrittenWithNothingAfterIt(string reachedAt, string origin)
    {
        Assert.Equal(origin, NoticeLinkBase.At(new Uri(reachedAt)).Origin);
        Assert.Null(NoticeLinkBase.None.Origin);
    }

    [Theory(DisplayName = "the screens are reached at an http or https origin and nothing after it")]
    [InlineData("ftp://screens.example")]
    [InlineData("https://screens.example/under")]
    [InlineData("https://screens.example/?a=b")]
    [InlineData("https://someone@screens.example")]
    public void TheOriginIsAnOriginAndNothingElse(string origin)
    {
        Assert.Throws<ArgumentException>(() => NoticeLinkBase.At(new Uri(origin)));
    }

    [Theory(DisplayName = "a link never leaves the origin")]
    [InlineData("settings")]
    [InlineData("//elsewhere.example/settings")]
    public void ALinkNeverLeavesTheOrigin(string path)
    {
        NoticeLinkBase links = NoticeLinkBase.At(new Uri("https://screens.example"));

        Assert.Throws<ArgumentException>(() => links.To(path));
    }
}
