using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class DeleteReservationEndpointTests
{
    private const string ReservationIdTextDescription =
        "A reservation is named by a UUID, and never by one that is all zeroes.";

    [Fact]
    public async Task AReservationThatWasCancelledIsThrownAwayAndIsGoneFromTheLedger()
    {
        await using var feature = new ReservationFeature();
        Reservation cancelled = feature.Booked(4001, state: ReservationState.Cancelled);
        Reservation beside = feature.Booked(4002, state: ReservationState.Cancelled);

        (HttpStatusCode status, JsonElement body) =
            await feature.DeleteAsync($"/api/reservations/{cancelled.Id.Value}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("status").GetBoolean());
        Assert.Equal(
            cancelled.Id.Value,
            body.GetProperty("data").GetProperty("reservationId").GetGuid());
        Assert.Equal([beside.Id], [.. feature.Reservations.Held.Select(reservation => reservation.Id)]);

        (HttpStatusCode after, _) = await feature.GetAsync($"/api/reservations/{cancelled.Id.Value}");

        Assert.Equal(HttpStatusCode.NotFound, after);
    }

    [Theory]
    [InlineData(ReservationState.Scheduled)]
    [InlineData(ReservationState.Conflict)]
    [InlineData(ReservationState.Cancelled)]
    [InlineData(ReservationState.Missed)]
    public async Task AReservationNoRecordingCameOfIsThrownAwayWhateverItStandsAs(ReservationState state)
    {
        await using var feature = new ReservationFeature();
        Reservation standing = feature.Booked(4001, state: state);

        (HttpStatusCode status, _) = await feature.DeleteAsync($"/api/reservations/{standing.Id.Value}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Empty(feature.Reservations.Held);
    }

    [Fact]
    public async Task AReservationThatFailedIsThrownAwayWhenSomebodyAsksForThatOne()
    {
        await using var feature = new ReservationFeature();
        Reservation failed = feature.Booked(
            4001,
            state: ReservationState.Scheduled,
            startedAt: ReservationFeature.Noon,
            outcome: RecordingOutcome.Failed);

        (HttpStatusCode status, _) = await feature.DeleteAsync($"/api/reservations/{failed.Id.Value}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Empty(feature.Reservations.Held);
    }

    [Fact]
    public async Task AReservationARecordingCameOfIsRefusedUntilThatRecordingIsThrownAway()
    {
        await using var feature = new ReservationFeature();
        Reservation recorded = feature.Booked(
            4001,
            startedAt: ReservationFeature.Noon,
            outcome: RecordingOutcome.Complete);
        feature.Reservations.RecordingCameOf(recorded);

        (HttpStatusCode status, JsonElement body) =
            await feature.DeleteAsync($"/api/reservations/{recorded.Id.Value}");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.False(body.GetProperty("status").GetBoolean());
        Assert.Equal("recordingCameOfIt", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.Equal(
            recorded.Id.Value,
            body.GetProperty("data").GetProperty("reservationId").GetGuid());
        Assert.Single(feature.Reservations.Held);

        feature.Reservations.RecordingThrownAwayFrom(recorded);

        (HttpStatusCode again, _) = await feature.DeleteAsync($"/api/reservations/{recorded.Id.Value}");

        Assert.Equal(HttpStatusCode.OK, again);
        Assert.Empty(feature.Reservations.Held);
    }

    [Fact]
    public async Task AReservationTakenUpBeforeItsRecordingIsWrittenDownIsRefused()
    {
        await using var feature = new ReservationFeature();
        Reservation claimed = feature.Booked(4001, startedAt: ReservationFeature.Noon);

        (HttpStatusCode status, JsonElement body) =
            await feature.DeleteAsync($"/api/reservations/{claimed.Id.Value}");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("turningIntoARecording", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.Single(feature.Reservations.Held);
    }

    [Fact]
    public async Task AReservationNobodyEverMadeIsAnAbsenceRatherThanARefusal()
    {
        await using var feature = new ReservationFeature();

        (HttpStatusCode status, JsonElement body) =
            await feature.DeleteAsync($"/api/reservations/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal("noSuchReservation", body.GetProperty("data").GetProperty("refusal").GetString());
    }

    [Fact]
    public async Task AReservationIsNeverNamedByAnIdentifierThatIsAllZeroes()
    {
        await using var feature = new ReservationFeature();
        Reservation standing = feature.Booked(4001, state: ReservationState.Cancelled);

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync($"/api/reservations/{Guid.Empty}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(ReservationIdTextDescription, body.GetProperty("message").GetString());
        Assert.Equal([standing.Id], [.. feature.Reservations.Held.Select(reservation => reservation.Id)]);
    }

    [Fact]
    public async Task SomethingThatIsNotAUuidNeverReachesTheEndpointAtAll()
    {
        await using var feature = new ReservationFeature();
        feature.Booked(4001, state: ReservationState.Cancelled);

        (HttpStatusCode status, _) = await feature.DeleteAsync("/api/reservations/not-a-reservation");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Single(feature.Reservations.Held);
    }

    [Fact]
    public async Task ADeleteCarryingNoBodyReachesTheEndpointWithoutNamingAContentType()
    {
        await using var feature = new ReservationFeature();
        Reservation cancelled = feature.Booked(4001, state: ReservationState.Cancelled);

        using var asking = new HttpRequestMessage(
            HttpMethod.Delete,
            new Uri($"/api/reservations/{cancelled.Id.Value}", UriKind.Relative));
        using HttpResponseMessage response = await feature.Client.SendAsync(asking);

        Assert.Null(asking.Content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ADeleteThatNamesNoOriginIsRefusedBecauseThatIsWhatStandsInForTheContentType()
    {
        await using var feature = new ReservationFeature();
        Reservation cancelled = feature.Booked(4001, state: ReservationState.Cancelled);
        feature.Client.DefaultRequestHeaders.Remove(HeaderNames.Origin);

        using var asking = new HttpRequestMessage(
            HttpMethod.Delete,
            new Uri($"/api/reservations/{cancelled.Id.Value}", UriKind.Relative));
        using HttpResponseMessage response = await feature.Client.SendAsync(asking);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Single(feature.Reservations.Held);
    }

    [Fact]
    public async Task ADeleteCarryingABodyThatIsNotJsonIsStillRefused()
    {
        await using var feature = new ReservationFeature();
        Reservation cancelled = feature.Booked(4001, state: ReservationState.Cancelled);

        using var asking = new HttpRequestMessage(
            HttpMethod.Delete,
            new Uri($"/api/reservations/{cancelled.Id.Value}", UriKind.Relative))
        {
            Content = new StringContent("anything=1", Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        using HttpResponseMessage response = await feature.Client.SendAsync(asking);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Single(feature.Reservations.Held);
    }

    [Fact]
    public async Task TheSurfaceTheScreenGeneratesFromDescribesTheDeleteAndEveryWayItIsRefused()
    {
        await using var factory = new TestingWebApplicationFactory();
        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        JsonNode discard = document["paths"]!["/api/reservations/{id}"]!["delete"]!;

        Assert.Equal("deleteReservation", discard["operationId"]!.GetValue<string>());
        Assert.Equal(
            ["200", "400", "401", "404", "409", "500"],
            discard["responses"]!.AsObject().Select(response => response.Key).Order(StringComparer.Ordinal));

        string[] refusals = [
            .. document["components"]!["schemas"]!["ReservationFailure"]!["enum"]!
                .AsArray()
                .Select(value => value!.GetValue<string>()),
        ];

        Assert.Contains("recordingCameOfIt", refusals, StringComparer.Ordinal);
        Assert.Contains("turningIntoARecording", refusals, StringComparer.Ordinal);
    }
}
