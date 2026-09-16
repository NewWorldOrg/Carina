using System.Text.Json;

using Carina.Contracts;
using Carina.Domain.Channels;

namespace Carina.Api.Tests.FeatureTest;

/// <summary>
/// A reservation is answered in broadcast types and counts. Which tuner would carry it is the
/// application's own business: naming the device on this surface would put a piece of the host's
/// hardware into a screen about broadcasts, and would make the answer change whenever the seats
/// are renumbered though nothing about the reservation moved.
/// </summary>
[Collection(FeatureTestCollection.Name)]
public sealed class ReservationSurfaceNamesNoTunerTests
{
    private const string Seat = "adapter-of-this-host";

    private const string NotPlacedYet = "adapter-nobody-has-placed";

    [Fact]
    public async Task NoReservationSurfaceNamesTheTunerThatWouldCarryIt()
    {
        await using var feature = new ReservationFeature();
        feature.Seating.Capacity = OneSeatNamed();
        feature.Announced(4001);
        feature.Announced(4002, serviceId: 1032);

        (_, JsonElement first) = await feature.PostAsync("/api/reservations", Asking(4001));
        (_, JsonElement second) = await feature.PostAsync("/api/reservations", Asking(4002, serviceId: 1032));
        Guid lost = second.GetProperty("data").GetProperty("reservation").GetProperty("id").GetGuid();

        (_, JsonElement detail) = await feature.GetAsync($"/api/reservations/{lost}");
        (_, JsonElement list) = await feature.GetAsync("/api/reservations");
        (_, JsonElement health) = await feature.GetAsync("/api/reservations/health");
        (_, JsonElement ledger) = await feature.GetAsync("/api/reservations/outcomes");
        JsonElement[] answered = [first, second, detail, list, health, ledger];

        Assert.Equal("contended", second.GetProperty("data").GetProperty("verdict").GetString());
        Assert.All(answered, answer => Assert.DoesNotContain(Seat, answer.GetRawText(), StringComparison.Ordinal));
        Assert.All(
            answered,
            answer => Assert.DoesNotContain(NotPlacedYet, answer.GetRawText(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheHealthCountsWhatIsInTheWayRatherThanNamingTheTunersItIsInTheWayOf()
    {
        await using var feature = new ReservationFeature();
        feature.Seating.Capacity = OneSeatNamed();
        feature.Announced(4001);
        feature.Announced(4002, serviceId: 1032);

        await feature.PostAsync("/api/reservations", Asking(4001));
        await feature.PostAsync("/api/reservations", Asking(4002, serviceId: 1032));

        (_, JsonElement health) = await feature.GetAsync("/api/reservations/health");
        JsonElement counted = health.GetProperty("data");

        Assert.Equal(
            ["asOf", "contended", "epgDiverged", "epgMissing", "receptionUnavailable"],
            counted.EnumerateObject().Select(field => field.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(1, counted.GetProperty("contended").GetInt32());
    }

    private static TunerCapacity OneSeatNamed()
        => new(
            [new TunerSeat(Seat, BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false)],
            [NotPlacedYet]);

    private static object Asking(int eventId, int serviceId = 1024)
        => new
        {
            programme = $"{ReservationFeature.Network}-{serviceId}-{eventId}",
            programmeStartsAt = ReservationFeature.Noon.AddHours(2),
        };
}
