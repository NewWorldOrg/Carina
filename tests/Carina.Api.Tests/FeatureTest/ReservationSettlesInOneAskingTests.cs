using System.Net;
using System.Text.Json;

namespace Carina.Api.Tests.FeatureTest;

/// <summary>
/// Asking for a reservation settles it. There is no word of confirmation to send afterwards and
/// nothing waits in between for one: the answer to the asking already says which broadcast is
/// recorded and which one lost, and reading the reservation back afterwards says the same thing.
/// </summary>
[Collection(FeatureTestCollection.Name)]
public sealed class ReservationSettlesInOneAskingTests
{
    [Fact]
    public async Task TheAskingSettlesTheReservationRatherThanAConfirmationAfterIt()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001);
        feature.Announced(4002, serviceId: 1032);

        await feature.PostAsync("/api/reservations", Asking(4001));
        (HttpStatusCode status, JsonElement made) = await feature.PostAsync(
            "/api/reservations",
            Asking(4002, serviceId: 1032));
        JsonElement settlement = made.GetProperty("data");
        Guid lost = settlement.GetProperty("reservation").GetProperty("id").GetGuid();

        (_, JsonElement read) = await feature.GetAsync($"/api/reservations/{lost}");

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("contended", settlement.GetProperty("verdict").GetString());
        Assert.Equal("conflict", settlement.GetProperty("reservation").GetProperty("standing").GetString());
        Assert.Equal("conflict", read.GetProperty("data").GetProperty("standing").GetString());
    }

    [Fact]
    public async Task NothingIsLeftWaitingToBeConfirmedWhenTheSeatsRunOut()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001);
        feature.Announced(4002, serviceId: 1032);

        await feature.PostAsync("/api/reservations", Asking(4001));
        await feature.PostAsync("/api/reservations", Asking(4002, serviceId: 1032));

        (_, JsonElement list) = await feature.GetAsync("/api/reservations");

        Assert.Equal(
            ["conflict", "scheduled"],
            list.GetProperty("data").GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("standing").GetString()!)
                .Order(StringComparer.Ordinal)
                .ToArray());
        Assert.Equal(
            2,
            feature.Reservations.Wrote.Count(written => written.StartsWith("add ", StringComparison.Ordinal)));
    }

    private static object Asking(int eventId, int serviceId = 1024)
        => new
        {
            programme = $"{ReservationFeature.Network}-{serviceId}-{eventId}",
            programmeStartsAt = ReservationFeature.Noon.AddHours(2),
        };
}
