using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Reservations;

using Microsoft.EntityFrameworkCore;

using Puppis.Contracts;

namespace Carina.Api.Tests.Feature.Reservations;

[Trait("Category", "Feature")]
public sealed class CancelReservationActionTests
{
    private static readonly DateTime Noon = ReservationFeature.Noon;

    [Fact(DisplayName = "cancelling a reservation answers with nobody displaced")]
    public async Task CancellingAReservationAnswersWithNobodyDisplaced()
    {
        await using var feature = new ReservationFeature();
        Reservation held = feature.Booked(4001, serviceId: 1024, priority: 50);
        feature.Booked(4002, serviceId: 1032, priority: 10, state: ReservationState.Conflict);

        (HttpStatusCode status, JsonElement cancelled) =
            await feature.PostAsync($"/api/reservations/{held.Id.Value}/cancel");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Empty(cancelled.GetProperty("data").GetProperty("displaced").EnumerateArray());
    }

    [Fact(DisplayName = "cancelling names a reservation that fell in the same recalculation for another reason")]
    public async Task CancellingNamesAReservationThatFellInTheSameRecalculationForAnotherReason()
    {
        await using var feature = new ReservationFeature(seats: 3);
        feature.Booked(4001, serviceId: 1024, priority: 50);
        Reservation fell = feature.Booked(4002, serviceId: 1032, priority: 10);
        Reservation cancelled = feature.Booked(4003, serviceId: 1040, priority: 5);
        feature.Seating.Capacity = ReservationFeature.Terrestrial(1);

        (HttpStatusCode status, JsonElement answered) =
            await feature.PostAsync($"/api/reservations/{cancelled.Id.Value}/cancel");
        JsonElement displaced =
            Assert.Single(answered.GetProperty("data").GetProperty("displaced").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(fell.Id.Value, displaced.GetProperty("id").GetGuid());
        Assert.Equal("conflict", Standing(displaced));
    }

    [Fact]
    public async Task CancellingKeepsTheReservationAndTakesItOutOfWhatIsCounted()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001, serviceId: 1024);
        feature.Announced(4002, serviceId: 1032);

        (_, JsonElement kept) = await feature.PostAsync("/api/reservations", Asking(4001, priority: 50));
        (_, JsonElement lost) = await feature.PostAsync(
            "/api/reservations",
            Asking(4002, serviceId: 1032, priority: 10));

        Assert.Equal("contended", lost.GetProperty("data").GetProperty("verdict").GetString());

        (HttpStatusCode status, JsonElement cancelled) = await feature.PostAsync(
            $"/api/reservations/{Identifier(kept)}/cancel");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("cancelled", Standing(cancelled.GetProperty("data").GetProperty("reservation")));
        Assert.Equal(JsonValueKind.Null, cancelled.GetProperty("data").GetProperty("verdict").ValueKind);
        Assert.Equal(2, feature.Reservations.Held.Count);

        (_, JsonElement freed) = await feature.GetAsync($"/api/reservations/{Identifier(lost)}");

        Assert.Equal("scheduled", Standing(freed.GetProperty("data")));
    }

    [Fact]
    public async Task AReservationBeingRecordedIsNotCancelledOutFromUnderTheRecording()
    {
        await using var feature = new ReservationFeature();
        Reservation booked = feature.Booked(4001, startedAt: Noon);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            $"/api/reservations/{booked.Id.Value}/cancel");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains(
            "being recorded right now",
            body.GetProperty("message").GetString()!,
            StringComparison.Ordinal);
        Assert.Equal(ReservationState.Scheduled, booked.State);
    }

    [Fact]
    public async Task CancellingSomethingAlreadyCancelledIsAnsweredRatherThanRepeated()
    {
        await using var feature = new ReservationFeature();
        Reservation booked = feature.Booked(4001, state: ReservationState.Cancelled);

        (HttpStatusCode status, _) = await feature.PostAsync($"/api/reservations/{booked.Id.Value}/cancel");

        Assert.Equal(HttpStatusCode.Conflict, status);
    }

    [Fact]
    public async Task CancellingAReservationNobodyMadeIsAnsweredAsNoSuchReservation()
    {
        await using var feature = new ReservationFeature();

        (HttpStatusCode status, _) = await feature.PostAsync($"/api/reservations/{Guid.NewGuid()}/cancel");

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task CancellingAReservationTellsTheScreensTheReservationsMoved()
    {
        await using var feature = new ReservationFeature();
        Reservation standing = feature.Booked(4001);
        feature.Events.Signalled.Clear();

        await feature.PostAsync($"/api/reservations/{standing.Id.Value}/cancel");

        Assert.Equal([AppEventName.Reservations], feature.Events.Signalled.Distinct());
    }

    [Fact]
    public async Task AReservationIsNeverNamedByAnIdentifierThatIsAllZeroes()
    {
        await using var feature = new ReservationFeature();

        using var asking = new HttpRequestMessage(
            new HttpMethod("POST"),
            new Uri($"/api/reservations/{Guid.Empty}/cancel", UriKind.Relative))
        {
            Content = new StringContent("""{"priority":50}""", System.Text.Encoding.UTF8, "application/json"),
        };
        using HttpResponseMessage response = await feature.Client.SendAsync(asking);

        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    private static object Asking(int eventId, int serviceId = 1024, int? priority = null)
        => new
        {
            programme = $"{ReservationFeature.Network}-{serviceId}-{eventId}",
            programmeStartsAt = Noon.AddHours(2),
            priority,
        };

    private static Guid Identifier(JsonElement settlement)
        => settlement.GetProperty("data").GetProperty("reservation").GetProperty("id").GetGuid();

    private static string Standing(JsonElement reservation)
        => reservation.GetProperty("standing").GetString()!;
}
