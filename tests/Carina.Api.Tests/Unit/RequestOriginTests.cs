using Carina.Api.Authentication;

using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.Unit;

public sealed class RequestOriginTests
{
    [Theory]
    [InlineData("http", "http://carina.example")]
    [InlineData("https", "https://carina.example")]
    [InlineData("ws", "http://carina.example")]
    [InlineData("wss", "https://carina.example")]
    [InlineData("WSS", "https://carina.example")]
    public void AnOriginNamesThisAppWhenItIsThePageTheWireWasOpenedFrom(string scheme, string origin)
    {
        HttpRequest request = Arriving(scheme, origin);

        Assert.True(RequestOrigin.NamesThisOne(request));
        Assert.False(RequestOrigin.NamesSomewhereElse(request));
    }

    [Theory]
    [InlineData("wss", "http://carina.example")]
    [InlineData("ws", "https://carina.example")]
    [InlineData("wss", "https://elsewhere.example")]
    [InlineData("wss", "wss://carina.example")]
    [InlineData("ftp", "http://carina.example")]
    [InlineData("https", "null")]
    public void AnOriginThatIsNotThePageThisAppServedNamesSomewhereElse(string scheme, string origin)
    {
        HttpRequest request = Arriving(scheme, origin);

        Assert.False(RequestOrigin.NamesThisOne(request));
        Assert.True(RequestOrigin.NamesSomewhereElse(request));
    }

    [Fact]
    public void ARequestNamingNoOriginNamesNeitherThisOneNorSomewhereElse()
    {
        HttpRequest request = Arriving("wss", null);

        Assert.False(RequestOrigin.NamesThisOne(request));
        Assert.False(RequestOrigin.NamesSomewhereElse(request));
    }

    private static HttpRequest Arriving(string scheme, string? origin)
    {
        DefaultHttpContext context = new();

        context.Request.Scheme = scheme;
        context.Request.Host = new HostString("carina.example");

        if (origin is not null)
        {
            context.Request.Headers[HeaderNames.Origin] = origin;
        }

        return context.Request;
    }
}
