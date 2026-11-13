using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

using Microsoft.EntityFrameworkCore;

using Puppis.Contracts;

namespace Carina.Api.Tests.Feature.Reservations;

[Trait("Category", "Feature")]
public sealed class CreateReservationActionTests
{
    private static readonly DateTime Noon = ReservationFeature.Noon;

    private const string Seat = "adapter-of-this-host";

    private const string NotPlacedYet = "adapter-nobody-has-placed";

    [Fact]
    public async Task AReservationIsAnsweredAsSecuredInTheSameBreathItIsMade()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/reservations", Asking(4001));
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("secured", data.GetProperty("verdict").GetString());
        Assert.Empty(data.GetProperty("instead").EnumerateArray());
        Assert.Equal("scheduled", Standing(data.GetProperty("reservation")));
        Assert.Single(feature.Reservations.Held);
    }

    [Fact]
    public async Task TheSoundTheGuideAnnouncedIsCopiedOntoTheReservationWhenItIsMade()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001, audio: AudioMode.DualMono, sounds: 2);

        (HttpStatusCode status, _) = await feature.PostAsync("/api/reservations", Asking(4001));
        Reservation made = Assert.Single(feature.Reservations.Held);

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal(AudioMode.DualMono, made.SnapshotAudio);
        Assert.Equal(2, made.SnapshotSounds);
    }

    [Fact]
    public async Task WhatTheGuideSaidIsCopiedOntoTheReservationRatherThanLookedUpLater()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001, name: "Harbour report", summary: "The tide today");

        (_, JsonElement body) = await feature.PostAsync("/api/reservations", Asking(4001));
        JsonElement programme = body.GetProperty("data").GetProperty("reservation").GetProperty("programme");

        Assert.Equal("Harbour report", programme.GetProperty("name").GetString());
        Assert.Equal("The tide today", programme.GetProperty("summary").GetString());
        Assert.Equal("Cast\nSomebody", programme.GetProperty("extended").GetString());
        Assert.Equal(1, programme.GetProperty("genres").GetArrayLength());
        Assert.Equal(Noon, programme.GetProperty("capturedAt").GetDateTime());

        feature.Programmes.Programmes.Clear();

        (HttpStatusCode status, JsonElement listed) = await feature.GetAsync("/api/reservations");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            "Harbour report",
            listed.GetProperty("data").GetProperty("items")[0].GetProperty("programme").GetProperty("name").GetString());
    }

    [Fact]
    public async Task ABroadcastWithNoAnnouncedEndIsGivenAProvisionalOneAndSaysSo()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001, endAnnounced: false);

        (_, JsonElement body) = await feature.PostAsync("/api/reservations", Asking(4001));
        JsonElement window = body.GetProperty("data").GetProperty("reservation").GetProperty("window");

        Assert.False(window.GetProperty("endAtConfirmed").GetBoolean());
        Assert.Equal(
            Noon.AddHours(2) + Reservation.ProvisionalLengthWhenTheEndIsNotAnnounced,
            window.GetProperty("endAt").GetDateTime());
    }

    [Fact]
    public async Task TheMarginsAskedForAreWhatWidenTheEffectiveWindow()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001);

        (_, JsonElement body) = await feature.PostAsync(
            "/api/reservations",
            new
            {
                programme = $"{ReservationFeature.Network}-1024-4001",
                programmeStartsAt = Noon.AddHours(2),
                marginBeforeSeconds = 10,
                marginAfterSeconds = 30,
            });
        JsonElement window = body.GetProperty("data").GetProperty("reservation").GetProperty("window");

        Assert.Equal(Noon.AddHours(2).AddSeconds(-10), window.GetProperty("effectiveStartAt").GetDateTime());
        Assert.Equal(Noon.AddHours(3).AddSeconds(30), window.GetProperty("effectiveEndAt").GetDateTime());
    }

    [Fact(DisplayName = "a reservation that says nothing about encoding is made asking for one")]
    public async Task AReservationThatSaysNothingAboutEncodingIsMadeAskingForOne()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/reservations", Asking(4001));

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.True(body.GetProperty("data").GetProperty("reservation")
            .GetProperty("encodeWhenRecorded").GetBoolean());
        Assert.True(feature.Reservations.Held[0].EncodeWhenRecorded);
    }

    [Fact(DisplayName = "a reservation can be made asking for no encode")]
    public async Task AReservationCanBeMadeAskingForNoEncode()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            "/api/reservations",
            new
            {
                programme = $"{ReservationFeature.Network}-1024-4001",
                programmeStartsAt = Noon.AddHours(2),
                encodeWhenRecorded = false,
            });

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.False(body.GetProperty("data").GetProperty("reservation")
            .GetProperty("encodeWhenRecorded").GetBoolean());
        Assert.False(feature.Reservations.Held[0].EncodeWhenRecorded);
    }

    [Fact]
    public async Task ABroadcastTheGuideDoesNotHoldIsNotReserved()
    {
        await using var feature = new ReservationFeature();

        (HttpStatusCode status, _) = await feature.PostAsync("/api/reservations", Asking(4001));

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Empty(feature.Reservations.Held);
    }

    [Fact]
    public async Task AnEventIdWithTheWrongStartNamesADifferentBroadcast()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001, startsAt: Noon.AddHours(2));

        (HttpStatusCode status, _) = await feature.PostAsync(
            "/api/reservations",
            new
            {
                programme = $"{ReservationFeature.Network}-1024-4001",
                programmeStartsAt = Noon.AddHours(5),
            });

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Empty(feature.Reservations.Held);
    }

    [Fact]
    public async Task AShadowOfSomebodyElsesBroadcastIsNotTheEntryToRecord()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001, shadow: true);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/reservations", Asking(4001));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("shadow", body.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Empty(feature.Reservations.Held);
    }

    [Fact]
    public async Task BrRv001AListingAMovedBroadcastSuppressesIsRefusedAndThePrimaryIsNamed()
    {
        await using var feature = new ReservationFeature(seats: 2);
        feature.Announced(4001, related: [new RelatedProgramme(ReservationFeature.Network, 1032, 5001, RelationKind.Moved)]);
        feature.Announced(5001, serviceId: 1032, running: ProgrammeRunning.Running);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/reservations", Asking(4001));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains($"{ReservationFeature.Network}-1032-5001", body.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Empty(feature.Reservations.Held);
    }

    [Fact]
    public async Task BrRv001TheRefusalOfAMovedListingSaysSoAndNamesThePrimaryInItsData()
    {
        await using var feature = new ReservationFeature(seats: 2);
        feature.Announced(4001, related: [new RelatedProgramme(ReservationFeature.Network, 1032, 5001, RelationKind.Moved)]);
        feature.Announced(5001, serviceId: 1032, running: ProgrammeRunning.Running);

        (_, JsonElement body) = await feature.PostAsync("/api/reservations", Asking(4001));
        JsonElement refused = body.GetProperty("data");

        Assert.Equal("programmeIsAMovedDuplicate", refused.GetProperty("refusal").GetString());
        Assert.Equal($"{ReservationFeature.Network}-1032-5001", refused.GetProperty("primary").GetProperty("programme").GetString());
        Assert.Equal(Noon.AddHours(2), refused.GetProperty("primary").GetProperty("startsAt").GetDateTime());
    }

    [Fact]
    public async Task BrRd010ThePrimaryListingOfAMovedBroadcastIsReservedAsTheBroadcastsPrimary()
    {
        await using var feature = new ReservationFeature(seats: 2);
        feature.Announced(4001, related: [new RelatedProgramme(ReservationFeature.Network, 1032, 5001, RelationKind.Moved)]);
        feature.Announced(5001, serviceId: 1032, running: ProgrammeRunning.Running);

        (HttpStatusCode status, JsonElement body) =
            await feature.PostAsync("/api/reservations", Asking(5001, serviceId: 1032));
        JsonElement group = body.GetProperty("data").GetProperty("reservation").GetProperty("broadcastGroup");

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("movementPrimary", group.GetProperty("role").GetString());
        Assert.Equal($"movement:{ReservationFeature.Network}-1024-4001", group.GetProperty("key").GetString());
    }

    [Fact]
    public async Task BrRd010ReservingOneSegmentOfARelayReservesTheSegmentsAfterItUnderOneKey()
    {
        await using var feature = new ReservationFeature(seats: 2);
        feature.Announced(4001, related: [new RelatedProgramme(ReservationFeature.Network, 1032, 5001, RelationKind.Relayed)]);
        feature.Announced(5001, serviceId: 1032, startsAt: Noon.AddHours(3));

        (HttpStatusCode status, JsonElement body) =
            await feature.PostAsync("/api/reservations", Asking(4001, priority: 30));

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal(2, feature.Reservations.Held.Count);
        Assert.All(feature.Reservations.Held, reservation =>
        {
            Assert.Equal(BroadcastGroupRole.RelaySegment, reservation.BroadcastGroupRole);
            Assert.Equal($"relay:{ReservationFeature.Network}-1024-4001", reservation.BroadcastGroupKey?.Value);
            Assert.Equal(30, reservation.Priority.Value);
            Assert.False(reservation.IsRuleBorn);
        });
        Assert.Equal(
            "relaySegment",
            body.GetProperty("data").GetProperty("reservation").GetProperty("broadcastGroup").GetProperty("role").GetString());
    }

    [Fact]
    public async Task BrRd010ASegmentAlreadyReservedIsNotReservedAgainWhenAnotherSegmentIsAskedFor()
    {
        await using var feature = new ReservationFeature(seats: 2);
        feature.Announced(4001, related: [new RelatedProgramme(ReservationFeature.Network, 1032, 5001, RelationKind.Relayed)]);
        feature.Announced(5001, serviceId: 1032, startsAt: Noon.AddHours(3));
        feature.Booked(5001, serviceId: 1032, startsAt: Noon.AddHours(3), state: ReservationState.Cancelled);

        (HttpStatusCode status, _) = await feature.PostAsync("/api/reservations", Asking(4001));

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal(2, feature.Reservations.Held.Count);
    }

    [Fact]
    public async Task ABroadcastThatIsAlreadyReservedIsNotReservedTwice()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001);
        feature.Booked(4001);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/reservations", Asking(4001));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("already reserved", body.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Equal("alreadyReserved", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").GetProperty("primary").ValueKind);
        Assert.Single(feature.Reservations.Held);
    }

    [Fact]
    public async Task ABroadcastReservedWhileThisOneWasBeingWorkedOutIsAnsweredTheSameWay()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001);
        feature.Reservations.RefusesToAdd = new DbUpdateException("the unique index says so", (Exception?)null);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/reservations", Asking(4001));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("already reserved", body.GetProperty("message").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePlaceACancellationHoldsIsWhyTheSameBroadcastIsRestoredRatherThanMadeAgain()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001);
        feature.Booked(4001, state: ReservationState.Cancelled);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/reservations", Asking(4001));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("restored", body.GetProperty("message").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheOneThatLosesTheOnlySeatIsAnsweredAsContendedAndToldWhatIsRecordedInstead()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001, serviceId: 1024);
        feature.Announced(4002, serviceId: 1032);

        (_, JsonElement kept) = await feature.PostAsync("/api/reservations", Asking(4001, priority: 50));
        (HttpStatusCode status, JsonElement lost) = await feature.PostAsync(
            "/api/reservations",
            Asking(4002, serviceId: 1032, priority: 10));
        JsonElement data = lost.GetProperty("data");

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("contended", data.GetProperty("verdict").GetString());
        Assert.Equal("conflict", Standing(data.GetProperty("reservation")));
        Assert.Equal(
            [kept.GetProperty("data").GetProperty("reservation").GetProperty("id").GetGuid()],
            data.GetProperty("instead").EnumerateArray().Select(entry => entry.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task AServiceWithNowhereToTuneIsAnsweredAsHavingNowhereRatherThanAsSecured()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001);
        feature.Tuning.Refuse(1024, TuningRefusal.NoSelectedChannel);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/reservations", Asking(4001));
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("unreachable", data.GetProperty("verdict").GetString());
        Assert.NotEqual("secured", data.GetProperty("verdict").GetString());
        Assert.True(data.GetProperty("reservation").GetProperty("reception").GetProperty("unavailable").GetBoolean());
        Assert.Equal(
            Noon,
            data.GetProperty("reservation").GetProperty("reception").GetProperty("since").GetDateTime());
    }

    [Fact]
    public async Task ATunerLedgerThatCannotBeReadWritesNothingAtAll()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001);
        feature.Tuning.Refuse(1024, TuningRefusal.LedgerUnreadable);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/reservations", Asking(4001));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Contains("cannot be counted", body.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Empty(feature.Reservations.Held);
        Assert.Empty(feature.Reservations.Wrote);
    }

    [Fact]
    public async Task TunersThatCannotBeCountedLeaveTheReservationUnmade()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001);
        feature.Seating.Capacity = null;

        (HttpStatusCode status, _) = await feature.PostAsync("/api/reservations", Asking(4001));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Empty(feature.Reservations.Held);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"programme":"nonsense","programmeStartsAt":"2026-08-24T14:00:00Z"}""")]
    [InlineData("""{"programme":"32736-1024-4001"}""")]
    [InlineData("""{"programme":"32736-1024-4001","programmeStartsAt":"2026-08-24T14:00:00Z","priority":0}""")]
    [InlineData("""{"programme":"32736-1024-4001","programmeStartsAt":"2026-08-24T14:00:00Z","priority":100}""")]
    [InlineData("""{"programme":"32736-1024-4001","programmeStartsAt":"2026-08-24T14:00:00Z","marginBeforeSeconds":-1}""")]
    [InlineData("""{"programme":"32736-1024-4001","programmeStartsAt":"2026-08-24T14:00:00Z","marginAfterSeconds":3601}""")]
    public async Task ARequestOutsideWhatCreatingAnswersIsRefused(string body)
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001);

        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await feature.Client.PostAsync(
            new Uri("/api/reservations", UriKind.Relative),
            content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(feature.Reservations.Held);
    }

    [Fact(DisplayName = "a new reservation that takes the only seat names the one it took it from")]
    public async Task ANewReservationThatTakesTheOnlySeatNamesTheOneItTookItFrom()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4002, serviceId: 1032);
        Reservation held = feature.Booked(4001, serviceId: 1024, priority: 10);

        (HttpStatusCode status, JsonElement made) = await feature.PostAsync(
            "/api/reservations",
            Asking(4002, serviceId: 1032, priority: 50));
        JsonElement data = made.GetProperty("data");

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("secured", data.GetProperty("verdict").GetString());
        Assert.Equal(
            [held.Id.Value],
            data.GetProperty("displaced").EnumerateArray().Select(entry => entry.GetProperty("id").GetGuid()));
    }

    [Fact(DisplayName = "the reservations a change displaced come back earliest first")]
    public async Task TheReservationsAChangeDisplacedComeBackEarliestFirst()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4003, serviceId: 1040, startsAt: Noon.AddHours(2), endsAt: Noon.AddHours(6));
        Reservation later = feature.Booked(4002, serviceId: 1032, startsAt: Noon.AddHours(4), priority: 10);
        Reservation earlier = feature.Booked(4001, serviceId: 1024, startsAt: Noon.AddHours(2), priority: 10);

        (_, JsonElement made) = await feature.PostAsync(
            "/api/reservations",
            Asking(4003, serviceId: 1040, priority: 50));

        Assert.Equal(
            [earlier.Id.Value, later.Id.Value],
            made.GetProperty("data").GetProperty("displaced").EnumerateArray()
                .Select(entry => entry.GetProperty("id").GetGuid()));
    }

    [Fact(DisplayName = "a segment reserved alongside names the reservation it displaced too")]
    public async Task ASegmentReservedAlongsideNamesTheReservationItDisplacedToo()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001, related: [new RelatedProgramme(ReservationFeature.Network, 1032, 5001, RelationKind.Relayed)]);
        feature.Announced(5001, serviceId: 1032, startsAt: Noon.AddHours(3));
        Reservation held = feature.Booked(6001, serviceId: 1040, startsAt: Noon.AddHours(3), priority: 10);

        (HttpStatusCode status, JsonElement made) =
            await feature.PostAsync("/api/reservations", Asking(4001, priority: 50));
        JsonElement data = made.GetProperty("data");

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal(3, feature.Reservations.Held.Count);
        Assert.Equal(
            [held.Id.Value],
            data.GetProperty("displaced").EnumerateArray().Select(entry => entry.GetProperty("id").GetGuid()));
    }

    [Fact(DisplayName = "the asked reservation is not named as displaced when a segment reserved alongside takes its seat")]
    public async Task TheAskedReservationIsNotNamedWhenASegmentReservedAlongsideTakesItsSeat()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001, related: [new RelatedProgramme(ReservationFeature.Network, 1032, 5001, RelationKind.Relayed)]);
        feature.Announced(5001, serviceId: 1032, startsAt: Noon.AddHours(1), endsAt: Noon.AddHours(2).AddMinutes(30));

        (HttpStatusCode status, JsonElement made) =
            await feature.PostAsync("/api/reservations", Asking(4001, priority: 50));

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal(2, feature.Reservations.Held.Count);
        Assert.Equal(
            ReservationState.Conflict,
            feature.Reservations.Held.Single(held => held.Id.Value == Identifier(made)).State);
        Assert.Empty(made.GetProperty("data").GetProperty("displaced").EnumerateArray());
    }

    [Fact(DisplayName = "reservations displaced at the same start come back in the order of their ids")]
    public async Task ReservationsDisplacedAtTheSameStartComeBackInTheOrderOfTheirIds()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4003, serviceId: 1040, startsAt: Noon.AddHours(2), endsAt: Noon.AddHours(4));
        Reservation one = feature.Booked(4001, serviceId: 1024, startsAt: Noon.AddHours(2), priority: 10);
        Reservation other = feature.Booked(4002, serviceId: 1032, startsAt: Noon.AddHours(2), priority: 10);

        (_, JsonElement made) = await feature.PostAsync(
            "/api/reservations",
            Asking(4003, serviceId: 1040, priority: 50));

        Assert.Equal(
            new[] { one.Id.Value, other.Id.Value }.Order(),
            made.GetProperty("data").GetProperty("displaced").EnumerateArray()
                .Select(entry => entry.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task ABroadcastReservedWhileItIsOnAirIsPromisedFromTheMomentItWasReserved()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001, startsAt: Noon.AddMinutes(-50), endsAt: Noon.AddMinutes(150));

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            "/api/reservations",
            new
            {
                programme = $"{ReservationFeature.Network}-1024-4001",
                programmeStartsAt = Noon.AddMinutes(-50),
                marginBeforeSeconds = 120,
                marginAfterSeconds = 180,
            });
        JsonElement reservation = body.GetProperty("data").GetProperty("reservation");
        JsonElement window = reservation.GetProperty("window");

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal(Noon, window.GetProperty("startAt").GetDateTime());
        Assert.Equal(0, window.GetProperty("marginBeforeSeconds").GetInt32());
        Assert.Equal(Noon, window.GetProperty("effectiveStartAt").GetDateTime());
        Assert.Equal(Noon.AddMinutes(150), window.GetProperty("endAt").GetDateTime());
        Assert.Equal(180, window.GetProperty("marginAfterSeconds").GetInt32());
        Assert.Equal(Noon.AddMinutes(153), window.GetProperty("effectiveEndAt").GetDateTime());
        Assert.Equal(
            Noon.AddMinutes(-50),
            reservation.GetProperty("programme").GetProperty("startsAt").GetDateTime());
    }

    [Fact]
    public async Task ABroadcastReservedBeforeItStartsIsPromisedTheMarginItAskedFor()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001, startsAt: Noon.AddMinutes(50), endsAt: Noon.AddMinutes(250));

        (_, JsonElement body) = await feature.PostAsync(
            "/api/reservations",
            new
            {
                programme = $"{ReservationFeature.Network}-1024-4001",
                programmeStartsAt = Noon.AddMinutes(50),
                marginBeforeSeconds = 120,
                marginAfterSeconds = 180,
            });
        JsonElement window = body.GetProperty("data").GetProperty("reservation").GetProperty("window");

        Assert.Equal(Noon.AddMinutes(50), window.GetProperty("startAt").GetDateTime());
        Assert.Equal(120, window.GetProperty("marginBeforeSeconds").GetInt32());
        Assert.Equal(Noon.AddMinutes(48), window.GetProperty("effectiveStartAt").GetDateTime());
    }

    [Fact]
    public async Task AMarginAskedForBeforeABroadcastThatIsAboutToStartIsTrimmedToTheMomentItWasReserved()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001, startsAt: Noon.AddMinutes(2), endsAt: Noon.AddMinutes(62));

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            "/api/reservations",
            new
            {
                programme = $"{ReservationFeature.Network}-1024-4001",
                programmeStartsAt = Noon.AddMinutes(2),
                marginBeforeSeconds = 300,
                marginAfterSeconds = 180,
            });
        JsonElement window = body.GetProperty("data").GetProperty("reservation").GetProperty("window");

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal(Noon.AddMinutes(2), window.GetProperty("startAt").GetDateTime());
        Assert.Equal(120, window.GetProperty("marginBeforeSeconds").GetInt32());
        Assert.Equal(Noon, window.GetProperty("effectiveStartAt").GetDateTime());
        Assert.Equal(Noon.AddMinutes(62), window.GetProperty("endAt").GetDateTime());
        Assert.Equal(180, window.GetProperty("marginAfterSeconds").GetInt32());
    }

    [Fact]
    public async Task MakingAReservationTellsTheScreensTheReservationsMoved()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001);
        feature.Events.Signalled.Clear();

        await feature.PostAsync("/api/reservations", Asking(4001));

        Assert.Equal([AppEventName.Reservations], feature.Events.Signalled.Distinct());
    }

    [Fact]
    public async Task TheAskingSettlesTheReservationRatherThanAConfirmationAfterIt()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001);
        feature.Announced(4002, serviceId: 1032);

        await feature.PostAsync("/api/reservations", AskingWithoutPriority(4001));
        (HttpStatusCode status, JsonElement made) = await feature.PostAsync(
            "/api/reservations",
            AskingWithoutPriority(4002, serviceId: 1032));
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

        await feature.PostAsync("/api/reservations", AskingWithoutPriority(4001));
        await feature.PostAsync("/api/reservations", AskingWithoutPriority(4002, serviceId: 1032));

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

    [Fact]
    public async Task NoReservationSurfaceNamesTheTunerThatWouldCarryIt()
    {
        await using var feature = new ReservationFeature();
        feature.Seating.Capacity = OneSeatNamed();
        feature.Announced(4001);
        feature.Announced(4002, serviceId: 1032);

        (_, JsonElement first) = await feature.PostAsync("/api/reservations", AskingWithoutPriority(4001));
        (_, JsonElement second) = await feature.PostAsync("/api/reservations", AskingWithoutPriority(4002, serviceId: 1032));
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

    private static object AskingWithoutPriority(int eventId, int serviceId = 1024)
        => new
        {
            programme = $"{ReservationFeature.Network}-{serviceId}-{eventId}",
            programmeStartsAt = ReservationFeature.Noon.AddHours(2),
        };

    private static TunerCapacity OneSeatNamed()
        => new(
            [new TunerSeat(Seat, BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false)],
            [NotPlacedYet]);
}
