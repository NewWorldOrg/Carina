using System.Net;

using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Api.Tests.FeatureTest;

public sealed class TicketInThePathTests
{
    private const string AName = "a-programme.ts";

    [Fact(DisplayName = "a player handed the ticket in the path is handed the recording, asks its headers and seeks on the same URL")]
    public async Task APlayerHandedTheTicketInThePathIsHandedTheRecordingAndSeeksOnTheSameUrl()
    {
        await using TicketedFeature feature = new();
        Recording recording = feature.Ended();
        string path = WithTicket(recording, await feature.TicketForAsync(recording.Id), AName);

        using HttpResponseMessage headers = await feature.AsAPlayerAsync(path, null, method: HttpMethod.Head);
        using HttpResponseMessage whole = await feature.AsAPlayerAsync(path, null, "bytes=0-");
        using HttpResponseMessage middle = await feature.AsAPlayerAsync(path, null, "bytes=1000-1999");

        Assert.Equal(HttpStatusCode.OK, headers.StatusCode);
        Assert.Equal(feature.Written.Length, headers.Content.Headers.ContentLength);
        Assert.Equal(HttpStatusCode.PartialContent, whole.StatusCode);
        Assert.Equal(feature.Written, await whole.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.PartialContent, middle.StatusCode);
        Assert.Equal(feature.Written[1000..2000], await middle.Content.ReadAsByteArrayAsync());
    }

    [Fact(DisplayName = "the ticket in the path hands over the source the query asks for")]
    public async Task TheTicketInThePathHandsOverTheSourceTheQueryAsksFor()
    {
        await using TicketedFeature feature = new();
        Recording recording = feature.Ended();
        byte[] artefact = feature.Encoded(recording, EncodeCodec.H264, EncodeCodec.H264);
        string ticket = await feature.TicketForAsync(recording.Id);

        using HttpResponseMessage encoded = await feature.AsAPlayerAsync(
            $"{WithTicket(recording, ticket, "a-programme.mp4")}?source=artefact",
            null);
        using HttpResponseMessage itself = await feature.AsAPlayerAsync(
            $"{WithTicket(recording, ticket, AName)}?source=recording",
            null);

        Assert.Equal(HttpStatusCode.OK, encoded.StatusCode);
        Assert.Equal("video/mp4", encoded.Content.Headers.ContentType?.MediaType);
        Assert.Equal(artefact, await encoded.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.OK, itself.StatusCode);
        Assert.Equal(feature.Written, await itself.Content.ReadAsByteArrayAsync());
    }

    [Theory(DisplayName = "the name at the end of the path is only for showing and is never read")]
    [InlineData("")]
    [InlineData("a-programme.mp4")]
    [InlineData("%E7%95%AA%E7%B5%84%20%E3%81%AE%E5%90%8D%E5%89%8D.ts")]
    [InlineData("two/segments.ts")]
    [InlineData("no-extension")]
    public async Task TheNameAtTheEndOfThePathIsOnlyForShowing(string name)
    {
        await using TicketedFeature feature = new();
        Recording recording = feature.Ended();

        using HttpResponseMessage answer = await feature.AsAPlayerAsync(
            WithTicket(recording, await feature.TicketForAsync(recording.Id), name),
            null);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(feature.Written, await answer.Content.ReadAsByteArrayAsync());
    }

    [Fact(DisplayName = "the ticket in the path opens the one recording it was issued for and no other")]
    public async Task TheTicketInThePathOpensTheOneRecordingItWasIssuedForAndNoOther()
    {
        await using TicketedFeature feature = new();
        Recording mine = feature.Ended();
        Recording another = feature.Ended();
        string ticket = await feature.TicketForAsync(mine.Id);

        using HttpResponseMessage entered = await feature.AsAPlayerAsync(WithTicket(mine, ticket, AName), null);
        using HttpResponseMessage elsewhere = await feature.AsAPlayerAsync(WithTicket(another, ticket, AName), null);

        Assert.Equal(HttpStatusCode.OK, entered.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, elsewhere.StatusCode);
        Assert.NotEqual(feature.Written, await elsewhere.Content.ReadAsByteArrayAsync());
    }

    [Fact(DisplayName = "something that was never issued in the ticket's place is refused before any byte leaves")]
    public async Task SomethingThatWasNeverIssuedInTheTicketsPlaceIsRefused()
    {
        await using TicketedFeature feature = new();
        Recording recording = feature.Ended();

        using HttpResponseMessage answer = await feature.AsAPlayerAsync(
            WithTicket(recording, new string('a', 43), AName),
            null);

        Assert.Equal(HttpStatusCode.Forbidden, answer.StatusCode);
        Assert.NotEqual(feature.Written, await answer.Content.ReadAsByteArrayAsync());
    }

    [Fact(DisplayName = "something that is not the shape of a ticket in its place is refused without being sent to a sign-in screen, whatever the header carries")]
    public async Task SomethingThatIsNotTheShapeOfATicketInItsPlaceIsRefused()
    {
        await using TicketedFeature feature = new();
        Recording recording = feature.Ended();
        string ticket = await feature.TicketForAsync(recording.Id);

        using HttpResponseMessage answer = await feature.AsAPlayerAsync(
            WithTicket(recording, "short", AName),
            ticket);

        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
        Assert.Null(answer.Headers.Location);
        Assert.Empty(await answer.Content.ReadAsByteArrayAsync());
    }

    [Theory(DisplayName = "the ticket in the path opens the bytes and nothing else under the same prefix")]
    [InlineData("play")]
    [InlineData("thumbnail")]
    [InlineData("scrub")]
    [InlineData("captions")]
    [InlineData("data-broadcast")]
    public async Task TheTicketInThePathOpensTheBytesAndNothingElse(string beneath)
    {
        await using TicketedFeature feature = new();
        Recording recording = feature.Ended();
        string ticket = await feature.TicketForAsync(recording.Id);

        using HttpResponseMessage answer = await feature.AsAPlayerAsync(
            $"/api/videos/{recording.Id.Wire}/{beneath}/with-ticket/{ticket}/{AName}",
            null);

        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
        Assert.Null(answer.Headers.Location);
    }

    [Fact(DisplayName = "a ticket in the query is refused on both surfaces, because it shows as the title in a player")]
    public async Task ATicketInTheQueryIsRefusedOnBothSurfaces()
    {
        await using TicketedFeature feature = new();
        Recording recording = feature.Ended();
        string ticket = await feature.TicketForAsync(recording.Id);

        using HttpResponseMessage plain = await feature.AsAPlayerAsync(
            $"/api/videos/{recording.Id.Wire}?ticket={ticket}",
            null);
        using HttpResponseMessage named = await feature.AsAPlayerAsync(
            $"/api/videos/{recording.Id.Wire}/with-ticket/short/{AName}?ticket={ticket}",
            null);

        Assert.Equal(HttpStatusCode.Unauthorized, plain.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, named.StatusCode);
    }

    [Fact(DisplayName = "the answer to a ticket in the path is never held by anything in front")]
    public async Task TheAnswerToATicketInThePathIsNeverHeldByAnythingInFront()
    {
        await using TicketedFeature feature = new();
        Recording recording = feature.Ended();

        using HttpResponseMessage answer = await feature.AsAPlayerAsync(
            WithTicket(recording, await feature.TicketForAsync(recording.Id), AName),
            null);

        Assert.Equal("no-store, private", answer.Headers.CacheControl?.ToString());
    }

    [Fact(DisplayName = "with every log turned up to its finest, no line carries the ticket in the path")]
    public async Task WithEveryLogTurnedUpNoLineCarriesTheTicketInThePath()
    {
        RecordedStartup heard = new();
        await using TicketedFeature feature = new(heard);
        Recording recording = feature.Ended();
        Recording another = feature.Ended();
        string ticket = await feature.TicketForAsync(recording.Id);

        using HttpResponseMessage entered = await feature.AsAPlayerAsync(WithTicket(recording, ticket, AName), null);
        using HttpResponseMessage elsewhere = await feature.AsAPlayerAsync(WithTicket(another, ticket, AName), null);

        Assert.Equal(HttpStatusCode.OK, entered.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, elsewhere.StatusCode);
        Assert.Contains(heard.Everything, line => line.Category.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        Assert.Empty(heard.Everything
            .Where(line => line.Text.Contains(ticket, StringComparison.Ordinal))
            .Select(line => $"{line.Category} {line.Level}")
            .Distinct());
    }

    private static string WithTicket(Recording recording, string ticket, string name)
        => $"/api/videos/{recording.Id.Wire}/with-ticket/{ticket}/{name}";
}
