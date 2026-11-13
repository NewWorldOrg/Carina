using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Reservations;

using Microsoft.EntityFrameworkCore;

namespace Carina.Api.Tests.Feature.Reservations;

[Trait("Category", "Feature")]
public sealed class RestoreReservationActionTests
{
    private static readonly DateTime Noon = ReservationFeature.Noon;

    [Fact(DisplayName = "restoring a reservation that takes the only seat names the one it took it from")]
    public async Task RestoringAReservationThatTakesTheOnlySeatNamesTheOneItTookItFrom()
    {
        await using var feature = new ReservationFeature();
        Reservation held = feature.Booked(4001, serviceId: 1024, priority: 10);
        Reservation cancelled = feature.Booked(
            4002,
            serviceId: 1032,
            priority: 50,
            state: ReservationState.Cancelled);

        (_, JsonElement restored) = await feature.PostAsync($"/api/reservations/{cancelled.Id.Value}/restore");
        JsonElement displaced =
            Assert.Single(restored.GetProperty("data").GetProperty("displaced").EnumerateArray());

        Assert.Equal(held.Id.Value, displaced.GetProperty("id").GetGuid());
        Assert.Equal("conflict", Standing(displaced));
    }

    [Fact]
    public async Task RestoringPutsAReservationBackThroughTheCalculationRatherThanStraightToSecured()
    {
        await using var feature = new ReservationFeature();
        Reservation held = feature.Booked(4001, serviceId: 1024, priority: 50);
        Reservation cancelled = feature.Booked(
            4002,
            serviceId: 1032,
            priority: 10,
            state: ReservationState.Cancelled);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            $"/api/reservations/{cancelled.Id.Value}/restore");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("contended", data.GetProperty("verdict").GetString());
        Assert.Equal("conflict", Standing(data.GetProperty("reservation")));
        Assert.Equal(
            [held.Id.Value],
            data.GetProperty("instead").EnumerateArray().Select(entry => entry.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task RestoringSomethingThatWasNeverCancelledIsRefused()
    {
        await using var feature = new ReservationFeature();
        Reservation booked = feature.Booked(4001);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            $"/api/reservations/{booked.Id.Value}/restore");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("was cancelled", body.GetProperty("message").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestoringAWindowThatHasAlreadyClosedWouldLeaveARowNothingRecords()
    {
        await using var feature = new ReservationFeature();
        Reservation booked = feature.Booked(
            4001,
            startsAt: Noon.AddHours(-5),
            state: ReservationState.Cancelled);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            $"/api/reservations/{booked.Id.Value}/restore");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("closed at", body.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Equal(ReservationState.Cancelled, booked.State);
    }

    [Fact]
    public async Task RestoringAReservationNobodyMadeIsAnsweredAsNoSuchReservation()
    {
        await using var feature = new ReservationFeature();

        (HttpStatusCode status, _) = await feature.PostAsync($"/api/reservations/{Guid.NewGuid()}/restore");

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task AReservationIsNeverNamedByAnIdentifierThatIsAllZeroes()
    {
        await using var feature = new ReservationFeature();

        using var asking = new HttpRequestMessage(
            new HttpMethod("POST"),
            new Uri($"/api/reservations/{Guid.Empty}/restore", UriKind.Relative))
        {
            Content = new StringContent("""{"priority":50}""", System.Text.Encoding.UTF8, "application/json"),
        };
        using HttpResponseMessage response = await feature.Client.SendAsync(asking);

        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    private static string Standing(JsonElement reservation)
        => reservation.GetProperty("standing").GetString()!;
}
