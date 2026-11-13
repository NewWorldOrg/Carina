using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Reservations;

namespace Carina.Api.Tests.Feature.Reservations;

[Trait("Category", "Feature")]
public sealed class GetReservationActionTests
{
    [Fact]
    public async Task AReservationWhoseBroadcastMovedSaysSoOnTheWireWithWhatItWasAndWhatItIs()
    {
        await using var feature = new ReservationFeature();
        Reservation moved = feature.Booked(4001, diverged: true);

        (HttpStatusCode status, JsonElement body) =
            await feature.GetAsync($"/api/reservations/{moved.Id.Value}");
        JsonElement epg = body.GetProperty("data").GetProperty("epg");
        JsonElement said = epg.GetProperty("detail").EnumerateArray().Single();

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(epg.GetProperty("diverged").GetBoolean());
        Assert.False(epg.GetProperty("programmeMissing").GetBoolean());
        Assert.Equal("startAt", said.GetProperty("field").GetString());
        Assert.Equal("12:00", said.GetProperty("before").GetString());
        Assert.Equal("12:05", said.GetProperty("after").GetString());
    }

    [Fact]
    public async Task AReservationWhoseBroadcastWentAwaySaysThatOnTheWireToo()
    {
        await using var feature = new ReservationFeature();
        Reservation gone = feature.Booked(4001, state: ReservationState.Cancelled, missing: true);

        (_, JsonElement body) = await feature.GetAsync($"/api/reservations/{gone.Id.Value}");
        JsonElement epg = body.GetProperty("data").GetProperty("epg");

        Assert.True(epg.GetProperty("programmeMissing").GetBoolean());
        Assert.False(epg.GetProperty("diverged").GetBoolean());
        Assert.Empty(epg.GetProperty("detail").EnumerateArray());
    }

    [Theory]
    [InlineData(ReservationCancellation.ByHand, "byHand")]
    [InlineData(ReservationCancellation.SameBroadcast, "sameBroadcast")]
    public async Task ACancelledReservationSaysWhyItWasCancelled(ReservationCancellation cancellation, string said)
    {
        await using var feature = new ReservationFeature();
        Reservation cancelled = feature.Booked(4001, state: ReservationState.Cancelled, cancellation: cancellation);

        (_, JsonElement body) = await feature.GetAsync($"/api/reservations/{cancelled.Id.Value}");

        Assert.Equal(said, body.GetProperty("data").GetProperty("cancellation").GetString());
    }

    [Fact]
    public async Task AReservationThatStandsSaysNothingOfACancellation()
    {
        await using var feature = new ReservationFeature();
        Reservation standing = feature.Booked(4001);

        (_, JsonElement body) = await feature.GetAsync($"/api/reservations/{standing.Id.Value}");

        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").GetProperty("cancellation").ValueKind);
    }

    [Fact]
    public async Task AReservationIsReadBackByTheIdentifierItWasGiven()
    {
        await using var feature = new ReservationFeature();
        Reservation booked = feature.Booked(4001);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(
            $"/api/reservations/{booked.Id.Value}");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(booked.Id.Value, data.GetProperty("id").GetGuid());
        Assert.Equal(
            $"{ReservationFeature.Network}-1024-4001",
            data.GetProperty("programme").GetProperty("id").GetString());
    }

    [Fact]
    public async Task ThereIsNoSuchReservationAsOneNobodyMade()
    {
        await using var feature = new ReservationFeature();

        (HttpStatusCode status, _) = await feature.GetAsync($"/api/reservations/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task AReservationIsNeverNamedByAnIdentifierThatIsAllZeroes()
    {
        await using var feature = new ReservationFeature();

        using var asking = new HttpRequestMessage(
            new HttpMethod("GET"),
            new Uri($"/api/reservations/{Guid.Empty}", UriKind.Relative))
        {
            Content = new StringContent("""{"priority":50}""", System.Text.Encoding.UTF8, "application/json"),
        };
        using HttpResponseMessage response = await feature.Client.SendAsync(asking);

        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }
}
