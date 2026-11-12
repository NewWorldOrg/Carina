using Carina.Api.Authentication;
using Carina.Domain.Auth;

using Microsoft.AspNetCore.Http;

namespace Carina.Api.Tests.Unit;

public sealed class OidcHandshakeTests
{
    [Fact]
    public void AMarkIsRecognisedUnderTheOneNameHoweverTheRequestArrived()
    {
        string mark = Unguessable.Issue();

        Assert.Equal(mark, OidcHandshake.MarkCarriedBy(Carrying(mark, secure: true)));
        Assert.Equal(mark, OidcHandshake.MarkCarriedBy(Carrying(mark, secure: false)));
    }

    [Fact]
    public void ARequestCarryingNoMarkAtAllIsMatchedAgainstNothing()
    {
        Assert.Null(OidcHandshake.MarkCarriedBy(new DefaultHttpContext().Request));
    }

    [Fact]
    public void SomethingThatCouldNotHaveBeenIssuedIsNoMarkWhicheverWayItArrived()
    {
        Assert.Null(OidcHandshake.MarkCarriedBy(Carrying("not-a-mark", secure: true)));
        Assert.Null(OidcHandshake.MarkCarriedBy(Carrying("not-a-mark", secure: false)));
    }

    private static HttpRequest Carrying(string mark, bool secure)
    {
        var context = new DefaultHttpContext();

        context.Request.Scheme = secure ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
        context.Request.Headers.Cookie = $"carina_oidc={mark}";

        return context.Request;
    }
}
