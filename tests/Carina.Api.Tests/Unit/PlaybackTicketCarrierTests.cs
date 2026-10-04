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
    public void APlayerHandedAUrlWithTheTicketInItsQueryOffersIt()
    {
        string ticket = Unguessable.Issue();

        Assert.Equal(ticket, PlaybackTicketCarrier.OfferedBy(InTheQuery($"?source=artefact&ticket={ticket}")));
    }

    [Fact]
    public void TheHeaderIsReadBeforeTheQuery()
    {
        string carried = Unguessable.Issue();
        HttpRequest request = Asking($"Bearer {carried}");
        request.QueryString = new QueryString($"?ticket={Unguessable.Issue()}");

        Assert.Equal(carried, PlaybackTicketCarrier.OfferedBy(request));
    }

    [Fact]
    public void AHeaderThatCarriesNoTicketIsNotPassedOverForTheQuery()
    {
        HttpRequest request = Asking("Basic not-base64!!");
        request.QueryString = new QueryString($"?ticket={Unguessable.Issue()}");

        Assert.Null(PlaybackTicketCarrier.OfferedBy(request));
    }

    [Fact]
    public void TwoTicketsInTheQueryOfferNothingRatherThanWhicheverWins()
    {
        string ticket = Unguessable.Issue();

        Assert.Null(PlaybackTicketCarrier.OfferedBy(InTheQuery($"?ticket={ticket}&ticket={ticket}")));
    }

    [Theory]
    [InlineData("?ticket=")]
    [InlineData("?ticket=short")]
    [InlineData("?ticket=..........................................%2E")]
    public void AQueryValueThatIsNotATicketOffersNothing(string query)
    {
        Assert.Null(PlaybackTicketCarrier.OfferedBy(InTheQuery(query)));
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

    private static string Basic(string user, string password)
        => $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"))}";

    private static HttpRequest InTheQuery(string query)
    {
        DefaultHttpContext context = new();
        context.Request.QueryString = new QueryString(query);

        return context.Request;
    }

    private static HttpRequest Asking(string authorization)
    {
        DefaultHttpContext context = new();
        context.Request.Headers[HeaderNames.Authorization] = authorization;

        return context.Request;
    }
}
