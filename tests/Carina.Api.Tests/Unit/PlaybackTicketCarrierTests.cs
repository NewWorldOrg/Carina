using System.Text;

using Carina.Api.Authentication;
using Carina.Domain.Auth;

using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.Unit;

public sealed class PlaybackTicketCarrierTests
{
    [Fact]
    public void APlayerThatSendsABearerTokenIsOfferingATicket()
    {
        string ticket = Unguessable.Issue();

        Assert.Equal(ticket, PlaybackTicketCarrier.OfferedBy(Asking($"Bearer {ticket}")));
    }

    [Fact]
    public void APlayerHandedAUrlWithCredentialsInItOffersTheTicketAsThePassword()
    {
        string ticket = Unguessable.Issue();

        Assert.Equal(ticket, PlaybackTicketCarrier.OfferedBy(Asking(Basic("ticket", ticket))));
    }

    [Fact]
    public void APlayerThatSendsNoUserNameStillOffersTheTicketAsThePassword()
    {
        string ticket = Unguessable.Issue();

        Assert.Equal(ticket, PlaybackTicketCarrier.OfferedBy(Asking(Basic(string.Empty, ticket))));
    }

    [Theory]
    [InlineData("bearer")]
    [InlineData("BEARER")]
    public void TheSchemeIsReadTheWayHttpReadsIt(string scheme)
    {
        string ticket = Unguessable.Issue();

        Assert.Equal(ticket, PlaybackTicketCarrier.OfferedBy(Asking($"{scheme} {ticket}")));
    }

    [Fact]
    public void ATicketInTheQueryStringIsNotOfferedBecauseTheQueryReachesEveryAccessLogInFront()
    {
        string ticket = Unguessable.Issue();
        DefaultHttpContext context = new();
        context.Request.QueryString = new QueryString($"?ticket={ticket}");

        Assert.Null(PlaybackTicketCarrier.OfferedBy(context.Request));
    }

    [Fact]
    public void TheTicketIsReadOffTheHeaderAloneSoAStaleCookieCannotTakeItAway()
    {
        string ticket = Unguessable.Issue();
        HttpRequest request = Asking($"Bearer {ticket}");
        request.Headers.Cookie = $"{SessionCookie.Name}=stale";

        Assert.Equal(ticket, PlaybackTicketCarrier.OfferedBy(request));
    }

    [Fact]
    public void ARequestWithoutAnAuthorizationHeaderOffersNothing()
    {
        Assert.Null(PlaybackTicketCarrier.OfferedBy(new DefaultHttpContext().Request));
    }

    [Fact]
    public void TwoAuthorizationHeadersOfferNothingRatherThanWhicheverWins()
    {
        string ticket = Unguessable.Issue();
        DefaultHttpContext context = new();
        context.Request.Headers.Authorization = new[] { $"Bearer {ticket}", $"Bearer {ticket}" };

        Assert.Null(PlaybackTicketCarrier.OfferedBy(context.Request));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Bearer")]
    [InlineData("Bearer ")]
    [InlineData("Digest something")]
    [InlineData("Basic")]
    [InlineData("Basic not-base64!!")]
    [InlineData("Negotiate abcdef")]
    public void AnythingThatIsNotATicketOffersNothing(string authorization)
    {
        Assert.Null(PlaybackTicketCarrier.OfferedBy(Asking(authorization)));
    }

    [Fact]
    public void BasicCredentialsWithoutASeparatorOfferNothing()
    {
        string carried = Convert.ToBase64String(Encoding.UTF8.GetBytes(Unguessable.Issue()));

        Assert.Null(PlaybackTicketCarrier.OfferedBy(Asking($"Basic {carried}")));
    }

    [Fact]
    public void BasicCredentialsWithNoPasswordOfferNothing()
    {
        Assert.Null(PlaybackTicketCarrier.OfferedBy(Asking(Basic("ticket", string.Empty))));
    }

    [Fact]
    public void AUserNameThatLooksLikeATicketIsNotOfferedBecauseTheTicketIsThePassword()
    {
        string ticket = Unguessable.Issue();

        Assert.Null(PlaybackTicketCarrier.OfferedBy(Asking(Basic(ticket, "x"))));
    }

    [Fact]
    public void AValueThatIsNotTheShapeOfATicketOffersNothing()
    {
        Assert.Null(PlaybackTicketCarrier.OfferedBy(Asking("Bearer short")));
        Assert.Null(PlaybackTicketCarrier.OfferedBy(Asking($"Bearer {new string('.', Unguessable.Length)}")));
    }

    [Fact]
    public void CredentialsAreReadFromTheFirstSeparatorOnSoAUserNameCarryingOneOffersNothing()
    {
        string ticket = Unguessable.Issue();

        Assert.Null(PlaybackTicketCarrier.OfferedBy(Asking(Basic("a:b", ticket))));
    }

    [Fact(DisplayName = "a player handed a URL with the ticket in its path offers the ticket from the path")]
    public void APlayerHandedAUrlWithTheTicketInItsPathOffersTheTicketFromThePath()
    {
        string ticket = Unguessable.Issue();

        Assert.Equal(ticket, PlaybackTicketCarrier.OfferedBy(InThePath(ticket)));
    }

    [Fact(DisplayName = "where the path has a place for the ticket, the header is not read, so a second ticket cannot stand in for it")]
    public void WhereThePathHasAPlaceForTheTicketTheHeaderIsNotRead()
    {
        HttpRequest request = InThePath("short");
        request.Headers[HeaderNames.Authorization] = $"Bearer {Unguessable.Issue()}";

        Assert.Null(PlaybackTicketCarrier.OfferedBy(request));
    }

    [Theory(DisplayName = "something in the ticket's place in the path that is not the shape of a ticket offers nothing")]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("...........................................")]
    public void SomethingInTheTicketsPlaceThatIsNotATicketOffersNothing(string carried)
    {
        Assert.Null(PlaybackTicketCarrier.OfferedBy(InThePath(carried)));
    }

    private static HttpRequest InThePath(string ticket)
    {
        DefaultHttpContext context = new();
        context.Request.RouteValues[PlaybackTicketCarrier.InThePath] = ticket;

        return context.Request;
    }

    private static string Basic(string user, string password)
        => $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"))}";

    private static HttpRequest Asking(string authorization)
    {
        DefaultHttpContext context = new();
        context.Request.Headers[HeaderNames.Authorization] = authorization;

        return context.Request;
    }
}
