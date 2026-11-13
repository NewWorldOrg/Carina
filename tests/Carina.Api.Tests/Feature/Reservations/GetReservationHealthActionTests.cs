using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Channels;
using Carina.Domain.Reservations;

using Puppis.Contracts;

namespace Carina.Api.Tests.Feature.Reservations;

[Trait("Category", "Feature")]
public sealed class GetReservationHealthActionTests
{
    private static readonly DateTime Noon = ReservationFeature.Noon;

    private const string Seat = "adapter-of-this-host";

    private const string NotPlacedYet = "adapter-nobody-has-placed";

    [Fact]
    public async Task TheHealthCountsWhatStandsInTheWayOfWhatIsStillAhead()
    {
        await using var feature = new ReservationFeature();
        feature.Booked(4001);
        feature.Booked(4002, state: ReservationState.Conflict);
        feature.Booked(4003, state: ReservationState.Conflict, startsAt: Noon.AddHours(-3));
        feature.Booked(4004, state: ReservationState.Cancelled);
        feature.Booked(4005, receptionUnavailable: true);
        feature.Booked(4006, diverged: true);
        feature.Booked(4007, diverged: true, acknowledgedAt: Noon);
        feature.Booked(4008, missing: true);
        feature.Booked(4009, missing: true, acknowledgedAt: Noon);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/reservations/health");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(Noon, data.GetProperty("asOf").GetDateTime());
        Assert.Equal(1, data.GetProperty("contended").GetInt32());
        Assert.Equal(1, data.GetProperty("receptionUnavailable").GetInt32());
        Assert.Equal(1, data.GetProperty("epgDiverged").GetInt32());
        Assert.Equal(1, data.GetProperty("epgMissing").GetInt32());
    }

    [Fact]
    public async Task AClearBillOfHealthIsAllZeroesRatherThanAbsent()
    {
        await using var feature = new ReservationFeature();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/reservations/health");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(0, data.GetProperty("contended").GetInt32());
        Assert.Equal(0, data.GetProperty("receptionUnavailable").GetInt32());
        Assert.Equal(0, data.GetProperty("epgDiverged").GetInt32());
        Assert.Equal(0, data.GetProperty("epgMissing").GetInt32());
    }

    [Fact]
    public async Task TheHealthCountsWhatIsInTheWayRatherThanNamingTheTunersItIsInTheWayOf()
    {
        await using var feature = new ReservationFeature();
        feature.Seating.Capacity = OneSeatNamed();
        feature.Announced(4001);
        feature.Announced(4002, serviceId: 1032);

        await feature.PostAsync("/api/reservations", AskingForTheSurface(4001));
        await feature.PostAsync("/api/reservations", AskingForTheSurface(4002, serviceId: 1032));

        (_, JsonElement health) = await feature.GetAsync("/api/reservations/health");
        JsonElement counted = health.GetProperty("data");

        Assert.Equal(
            ["asOf", "contended", "epgDiverged", "epgMissing", "receptionUnavailable"],
            counted.EnumerateObject().Select(field => field.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(1, counted.GetProperty("contended").GetInt32());
    }

    [Fact]
    public async Task TheHealthIsNotReachedWithoutASession()
    {
        await using var feature = new ReservationFeature();
        feature.Client.DefaultRequestHeaders.Authorization = null;

        (HttpStatusCode status, _) = await feature.GetAsync("/api/reservations/health");

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    private static TunerCapacity OneSeatNamed()
        => new(
            [new TunerSeat(Seat, BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false)],
            [NotPlacedYet]);

    private static object AskingForTheSurface(int eventId, int serviceId = 1024)
        => new
        {
            programme = $"{ReservationFeature.Network}-{serviceId}-{eventId}",
            programmeStartsAt = ReservationFeature.Noon.AddHours(2),
        };
}
