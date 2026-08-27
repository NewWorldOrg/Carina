using System.Net;
using System.Text.Json;

using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;

using Microsoft.EntityFrameworkCore;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class ReservationEndpointTests
{
    private static readonly DateTime Noon = ReservationFeature.Noon;

    [Fact]
    public async Task ThePageSaysHowManyReservationsThereAreAndWhichPageThisIs()
    {
        await using var feature = new ReservationFeature();
        feature.Booked(4001);
        feature.Booked(4002);
        feature.Booked(4003);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/reservations?perPage=2");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(3, data.GetProperty("total").GetInt32());
        Assert.Equal(1, data.GetProperty("currentPage").GetInt32());
        Assert.Equal(2, data.GetProperty("lastPage").GetInt32());
        Assert.Equal(2, data.GetProperty("perPage").GetInt32());
        Assert.Equal(2, data.GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("from=2026-01-01T00:00:00Z&to=2027-06-01T00:00:00Z")]
    [InlineData("from=2026-08-24T12:00:00Z&to=2026-08-24T11:00:00Z")]
    [InlineData("standing=99")]
    [InlineData("origin=99")]
    [InlineData("sort=99")]
    [InlineData("channel=nonsense")]
    [InlineData("keyword=a")]
    public async Task ARequestOutsideWhatTheListAnswersIsRefused(string query)
    {
        await using var feature = new ReservationFeature();
        feature.Booked(4001);

        (HttpStatusCode status, _) = await feature.GetAsync("/api/reservations?" + query);

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task APageSizeOverTheCeilingIsCutDownToItAndAnsweredAsTheSizeThatWasUsed()
    {
        await using var feature = new ReservationFeature();

        foreach (int eventId in Enumerable.Range(1, ReservationQuery.MostPerPage + 1))
        {
            feature.Booked(eventId);
        }

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(
            $"/api/reservations?perPage={ReservationQuery.MostPerPage + 1}");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(ReservationQuery.MostPerPage + 1, data.GetProperty("total").GetInt32());
        Assert.Equal(ReservationQuery.MostPerPage, data.GetProperty("items").GetArrayLength());
        Assert.Equal(ReservationQuery.MostPerPage, data.GetProperty("perPage").GetInt32());
        Assert.Equal(2, data.GetProperty("lastPage").GetInt32());
    }

    [Fact]
    public async Task APageSizeBelowOneIsAnsweredAsTheSizeThatWasUsedInstead()
    {
        await using var feature = new ReservationFeature();
        feature.Booked(4001);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/reservations?perPage=0");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            ReservationQuery.DefaultPerPage,
            body.GetProperty("data").GetProperty("perPage").GetInt32());
    }

    [Fact]
    public async Task AReservationSomebodyCancelledIsStillOnTheList()
    {
        await using var feature = new ReservationFeature();
        feature.Booked(4001, state: ReservationState.Cancelled);

        (_, JsonElement body) = await feature.GetAsync("/api/reservations?standing=cancelled");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(1, data.GetProperty("total").GetInt32());
        Assert.Equal("cancelled", Standing(data.GetProperty("items")[0]));
    }

    [Fact]
    public async Task TheStandingsAskedForAreTheOnlyOnesAnswered()
    {
        await using var feature = new ReservationFeature();
        feature.Booked(4001);
        feature.Booked(4002, state: ReservationState.Conflict);
        feature.Booked(4003, startedAt: Noon);
        feature.Booked(4004, startedAt: Noon, outcome: RecordingOutcome.Complete);

        (_, JsonElement body) = await feature.GetAsync("/api/reservations?standing=conflict&standing=recording");
        JsonElement items = body.GetProperty("data").GetProperty("items");

        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal(
            ["conflict", "recording"],
            items.EnumerateArray().Select(Standing).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AReservationARuleMadeAndOneAHandMadeAreToldApartByOrigin()
    {
        await using var feature = new ReservationFeature();
        feature.Booked(4001);
        feature.Booked(4002, ruleId: RuleId.New());

        (_, JsonElement byHand) = await feature.GetAsync("/api/reservations?origin=byHand");
        (_, JsonElement byRule) = await feature.GetAsync("/api/reservations?origin=byRule");

        Assert.Equal(1, byHand.GetProperty("data").GetProperty("total").GetInt32());
        Assert.Equal("byHand", byHand.GetProperty("data").GetProperty("items")[0].GetProperty("origin").GetString());
        Assert.Equal(1, byRule.GetProperty("data").GetProperty("total").GetInt32());
        Assert.Equal("byRule", byRule.GetProperty("data").GetProperty("items")[0].GetProperty("origin").GetString());
    }

    [Fact]
    public async Task OnlyTheChannelsAskedForComeBack()
    {
        await using var feature = new ReservationFeature();
        feature.Booked(4001, serviceId: 1024);
        feature.Booked(4002, serviceId: 1032);

        (_, JsonElement body) = await feature.GetAsync(
            $"/api/reservations?channel={ReservationFeature.Network}-1032");
        JsonElement items = body.GetProperty("data").GetProperty("items");

        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(1032, items[0].GetProperty("programme").GetProperty("serviceId").GetInt32());
    }

    [Fact]
    public async Task AKeywordReadsTheSnapshotTheReservationKeptRatherThanTheGuide()
    {
        await using var feature = new ReservationFeature();
        feature.Booked(4001, name: "Harbour report", summary: "The tide today");
        feature.Booked(4002, name: "Kitchen notes", summary: "Summer vegetables");

        (_, JsonElement byName) = await feature.GetAsync("/api/reservations?keyword=harbour");
        (_, JsonElement bySummary) = await feature.GetAsync("/api/reservations?keyword=vegetables");

        Assert.Equal(1, byName.GetProperty("data").GetProperty("total").GetInt32());
        Assert.Equal(
            "Harbour report",
            byName.GetProperty("data").GetProperty("items")[0].GetProperty("programme").GetProperty("name").GetString());
        Assert.Equal(1, bySummary.GetProperty("data").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ASpanNarrowsTheListToWhatStartsInsideIt()
    {
        await using var feature = new ReservationFeature();
        feature.Booked(4001, startsAt: Noon.AddHours(2));
        feature.Booked(4002, startsAt: Noon.AddHours(20));

        (_, JsonElement body) = await feature.GetAsync(
            "/api/reservations?from=2026-08-24T13:00:00Z&to=2026-08-24T16:00:00Z");

        Assert.Equal(1, body.GetProperty("data").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task TheListIsSortedTheWayItWasAskedFor()
    {
        await using var feature = new ReservationFeature();
        feature.Booked(4001, priority: 10, startsAt: Noon.AddHours(2));
        feature.Booked(4002, priority: 50, startsAt: Noon.AddHours(4));

        (_, JsonElement ascending) = await feature.GetAsync("/api/reservations?sort=priority");
        (_, JsonElement descending) = await feature.GetAsync("/api/reservations?sort=priority&descending=true");

        Assert.Equal(10, Priorities(ascending).First());
        Assert.Equal(50, Priorities(descending).First());
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
    public async Task ABroadcastThatIsAlreadyReservedIsNotReservedTwice()
    {
        await using var feature = new ReservationFeature();
        feature.Announced(4001);
        feature.Booked(4001);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/reservations", Asking(4001));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("already reserved", body.GetProperty("message").GetString()!, StringComparison.Ordinal);
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

    [Theory]
    [InlineData("GET", "")]
    [InlineData("PATCH", "")]
    [InlineData("POST", "/cancel")]
    [InlineData("POST", "/restore")]
    public async Task AReservationIsNeverNamedByAnIdentifierThatIsAllZeroes(string method, string tail)
    {
        await using var feature = new ReservationFeature();

        using var asking = new HttpRequestMessage(
            new HttpMethod(method),
            new Uri($"/api/reservations/{Guid.Empty}{tail}", UriKind.Relative))
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

    private static IEnumerable<int> Priorities(JsonElement body)
        => body.GetProperty("data").GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("priority").GetInt32());
}
