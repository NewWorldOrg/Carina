using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Channels;
using Carina.Domain.Reservations;

using Microsoft.EntityFrameworkCore;

namespace Carina.Api.Tests.Feature.Reservations;

[Trait("Category", "Feature")]
public sealed class ReviseReservationActionTests
{
    private static readonly DateTime Noon = ReservationFeature.Noon;

    [Fact(DisplayName = "what a reservation asks about encoding is changed on its own, leaving the margins where they were")]
    public async Task WhatAReservationAsksAboutEncodingIsChangedOnItsOwn()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001);

        (_, JsonElement made) = await feature.PostAsync(
            "/api/reservations",
            new
            {
                programme = $"{ReservationFeature.Network}-1024-4001",
                programmeStartsAt = Noon.AddHours(2),
                marginBeforeSeconds = 10,
                marginAfterSeconds = 30,
            });

        (HttpStatusCode status, JsonElement revised) = await feature.PatchAsync(
            $"/api/reservations/{Identifier(made)}",
            new { encodeWhenRecorded = false });
        JsonElement reservation = revised.GetProperty("data").GetProperty("reservation");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(reservation.GetProperty("encodeWhenRecorded").GetBoolean());
        Assert.Equal(10, reservation.GetProperty("window").GetProperty("marginBeforeSeconds").GetInt32());
        Assert.Equal(30, reservation.GetProperty("window").GetProperty("marginAfterSeconds").GetInt32());
    }

    [Fact]
    public async Task RaisingAPriorityHandsTheSeatOverAndSaysWhatChanged()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001, serviceId: 1024);
        feature.Announced(4002, serviceId: 1032);

        (_, JsonElement first) = await feature.PostAsync("/api/reservations", Asking(4001, priority: 50));
        (_, JsonElement second) = await feature.PostAsync(
            "/api/reservations",
            Asking(4002, serviceId: 1032, priority: 10));

        Guid loser = Identifier(second);

        (HttpStatusCode status, JsonElement raised) = await feature.PatchAsync(
            $"/api/reservations/{loser}",
            new { priority = 90 });
        JsonElement data = raised.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("secured", data.GetProperty("verdict").GetString());
        Assert.Equal(90, data.GetProperty("reservation").GetProperty("priority").GetInt32());

        (_, JsonElement dropped) = await feature.GetAsync($"/api/reservations/{Identifier(first)}");

        Assert.Equal("conflict", Standing(dropped.GetProperty("data")));
    }

    [Fact(DisplayName = "raising a priority names the reservation that fell into contention for it")]
    public async Task RaisingAPriorityNamesTheReservationThatFellIntoContentionForIt()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001, serviceId: 1024);
        feature.Announced(4002, serviceId: 1032);

        (_, JsonElement first) = await feature.PostAsync("/api/reservations", Asking(4001, priority: 50));
        (_, JsonElement second) = await feature.PostAsync(
            "/api/reservations",
            Asking(4002, serviceId: 1032, priority: 10));

        (_, JsonElement raised) = await feature.PatchAsync(
            $"/api/reservations/{Identifier(second)}",
            new { priority = 90 });
        JsonElement displaced = Assert.Single(raised.GetProperty("data").GetProperty("displaced").EnumerateArray());

        Assert.Empty(second.GetProperty("data").GetProperty("displaced").EnumerateArray());
        Assert.Equal(Identifier(first), displaced.GetProperty("id").GetGuid());
        Assert.Equal("conflict", Standing(displaced));
        Assert.Empty(raised.GetProperty("data").GetProperty("instead").EnumerateArray());
    }

    [Fact(DisplayName = "lowering a priority into contention names what is recorded instead and nobody as displaced")]
    public async Task LoweringAPriorityIntoContentionNamesNobodyAsDisplaced()
    {
        await using var feature = new ReservationFeature();
        Reservation held = feature.Booked(4001, serviceId: 1024, priority: 50);
        Reservation waiting = feature.Booked(4002, serviceId: 1032, priority: 10, state: ReservationState.Conflict);

        (_, JsonElement lowered) = await feature.PatchAsync(
            $"/api/reservations/{held.Id.Value}",
            new { priority = 1 });
        JsonElement data = lowered.GetProperty("data");

        Assert.Equal("contended", data.GetProperty("verdict").GetString());
        Assert.Equal(
            [waiting.Id.Value],
            data.GetProperty("instead").EnumerateArray().Select(entry => entry.GetProperty("id").GetGuid()));
        Assert.Empty(data.GetProperty("displaced").EnumerateArray());
    }

    [Fact(DisplayName = "a reservation left with nowhere to tune by a change is not named as displaced")]
    public async Task AReservationLeftWithNowhereToTuneByAChangeIsNotNamedAsDisplaced()
    {
        await using var feature = new ReservationFeature(seats: 2);
        Reservation revised = feature.Booked(4001, serviceId: 1024, priority: 50);
        Reservation stranded = feature.Booked(4002, serviceId: 1032, priority: 10);
        feature.Tuning.Refuse(1032, TuningRefusal.NoSelectedChannel);

        (_, JsonElement raised) = await feature.PatchAsync(
            $"/api/reservations/{revised.Id.Value}",
            new { priority = 90 });

        Assert.True(stranded.ReceptionUnavailable);
        Assert.Empty(raised.GetProperty("data").GetProperty("displaced").EnumerateArray());
    }

    [Fact]
    public async Task ChangingOneMarginLeavesTheOtherWhereItWas()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001);

        (_, JsonElement made) = await feature.PostAsync(
            "/api/reservations",
            new
            {
                programme = $"{ReservationFeature.Network}-1024-4001",
                programmeStartsAt = Noon.AddHours(2),
                marginBeforeSeconds = 10,
                marginAfterSeconds = 30,
            });

        (HttpStatusCode status, JsonElement revised) = await feature.PatchAsync(
            $"/api/reservations/{Identifier(made)}",
            new { marginBeforeSeconds = 120 });
        JsonElement window = revised.GetProperty("data").GetProperty("reservation").GetProperty("window");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(120, window.GetProperty("marginBeforeSeconds").GetInt32());
        Assert.Equal(30, window.GetProperty("marginAfterSeconds").GetInt32());
    }

    [Fact]
    public async Task ChangingOnlyTheMarginAfterLeavesTheOneBeforeWhereItWas()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001);

        (_, JsonElement made) = await feature.PostAsync(
            "/api/reservations",
            new
            {
                programme = $"{ReservationFeature.Network}-1024-4001",
                programmeStartsAt = Noon.AddHours(2),
                marginBeforeSeconds = 10,
                marginAfterSeconds = 30,
            });

        (HttpStatusCode status, JsonElement revised) = await feature.PatchAsync(
            $"/api/reservations/{Identifier(made)}",
            new { marginAfterSeconds = 300 });
        JsonElement window = revised.GetProperty("data").GetProperty("reservation").GetProperty("window");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(10, window.GetProperty("marginBeforeSeconds").GetInt32());
        Assert.Equal(300, window.GetProperty("marginAfterSeconds").GetInt32());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"priority":0}""")]
    [InlineData("""{"priority":100}""")]
    [InlineData("""{"marginBeforeSeconds":-1}""")]
    [InlineData("""{"marginAfterSeconds":3601}""")]
    public async Task ARequestOutsideWhatChangingAnswersIsRefused(string body)
    {
        await using var feature = new ReservationFeature();
        Reservation booked = feature.Booked(4001);

        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await feature.Client.PatchAsync(
            new Uri($"/api/reservations/{booked.Id.Value}", UriKind.Relative),
            content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(feature.Reservations.Wrote);
    }

    [Fact]
    public async Task AReservationBeingRecordedIsNotChangedFromUnderTheRecording()
    {
        await using var feature = new ReservationFeature();
        Reservation booked = feature.Booked(4001, startedAt: Noon);

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync(
            $"/api/reservations/{booked.Id.Value}",
            new { priority = 90 });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains(
            "being recorded right now",
            body.GetProperty("message").GetString()!,
            StringComparison.Ordinal);
        Assert.Empty(feature.Reservations.Wrote);
    }

    [Theory]
    [InlineData(ReservationState.Cancelled)]
    [InlineData(ReservationState.Missed)]
    public async Task AReservationThatIsNoLongerWaitingForATunerIsNotChanged(ReservationState state)
    {
        await using var feature = new ReservationFeature();
        Reservation booked = feature.Booked(4001, state: state);

        (HttpStatusCode status, _) = await feature.PatchAsync(
            $"/api/reservations/{booked.Id.Value}",
            new { priority = 90 });

        Assert.Equal(HttpStatusCode.Conflict, status);
    }

    [Fact]
    public async Task ChangingAReservationNobodyMadeIsAnsweredAsNoSuchReservation()
    {
        await using var feature = new ReservationFeature();

        (HttpStatusCode status, _) = await feature.PatchAsync(
            $"/api/reservations/{Guid.NewGuid()}",
            new { priority = 90 });

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task APriorityIsNotWrittenWhenTheTunersCannotBeCounted()
    {
        await using var feature = new ReservationFeature();
        Reservation booked = feature.Booked(4001, priority: 10);
        feature.Seating.Capacity = null;

        (HttpStatusCode status, _) = await feature.PatchAsync(
            $"/api/reservations/{booked.Id.Value}",
            new { priority = 90 });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Empty(feature.Reservations.Wrote);
    }

    [Fact]
    public async Task ChangingTheMarginsOfAReservationDoesNotMoveThePromiseItWasMadeWith()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001, startsAt: Noon.AddMinutes(-50), endsAt: Noon.AddMinutes(150));

        (_, JsonElement made) = await feature.PostAsync(
            "/api/reservations",
            new
            {
                programme = $"{ReservationFeature.Network}-1024-4001",
                programmeStartsAt = Noon.AddMinutes(-50),
                marginBeforeSeconds = 120,
                marginAfterSeconds = 180,
            });

        (HttpStatusCode status, JsonElement revised) = await feature.PatchAsync(
            $"/api/reservations/{Identifier(made)}",
            new { marginAfterSeconds = 300 });
        JsonElement window = revised.GetProperty("data").GetProperty("reservation").GetProperty("window");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(Noon, window.GetProperty("startAt").GetDateTime());
        Assert.Equal(Noon, window.GetProperty("effectiveStartAt").GetDateTime());
        Assert.Equal(300, window.GetProperty("marginAfterSeconds").GetInt32());
    }

    [Fact]
    public async Task AReservationIsNeverNamedByAnIdentifierThatIsAllZeroes()
    {
        await using var feature = new ReservationFeature();

        using var asking = new HttpRequestMessage(
            new HttpMethod("PATCH"),
            new Uri($"/api/reservations/{Guid.Empty}", UriKind.Relative))
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
