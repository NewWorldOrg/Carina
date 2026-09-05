using System.Net;
using System.Text.Json;

using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class ReservationOutcomeEndpointTests
{
    private static readonly DateTime Noon = ReservationFeature.Noon;

    [Fact]
    public async Task ThePageSaysHowManyOutcomesThereAreAndWhichPageThisIs()
    {
        await using var feature = new ReservationFeature();
        feature.Recorded(feature.Booked(4001), ReservationOutcomeKind.Missed, Noon.AddHours(1));
        feature.Recorded(feature.Booked(4002), ReservationOutcomeKind.Missed, Noon.AddHours(2));
        feature.Recorded(feature.Booked(4003), ReservationOutcomeKind.Missed, Noon.AddHours(3));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/reservations/outcomes?perPage=2");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(3, data.GetProperty("total").GetInt32());
        Assert.Equal(1, data.GetProperty("currentPage").GetInt32());
        Assert.Equal(2, data.GetProperty("lastPage").GetInt32());
        Assert.Equal(2, data.GetProperty("perPage").GetInt32());
        Assert.Equal(
            [Noon.AddHours(3), Noon.AddHours(2)],
            data.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("occurredAt").GetDateTime()));
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("from=2026-01-01T00:00:00Z&to=2027-06-01T00:00:00Z")]
    [InlineData("from=2026-08-24T12:00:00Z&to=2026-08-24T11:00:00Z")]
    [InlineData("kind=99")]
    [InlineData("kind=nonsense")]
    [InlineData("channel=nonsense")]
    [InlineData("rule=00000000-0000-0000-0000-000000000000")]
    [InlineData("rule=nonsense")]
    public async Task ARequestOutsideWhatTheLedgerAnswersIsRefused_BR_RV_003(string query)
    {
        await using var feature = new ReservationFeature();
        feature.Recorded(feature.Booked(4001), ReservationOutcomeKind.Missed);

        (HttpStatusCode status, _) = await feature.GetAsync("/api/reservations/outcomes?" + query);

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task APageSizeOverTheCeilingIsCutDownToItAndAnsweredAsTheSizeThatWasUsed_BR_RV_003()
    {
        await using var feature = new ReservationFeature();

        foreach (int eventId in Enumerable.Range(1, ReservationOutcomeQuery.MostPerPage + 1))
        {
            feature.Recorded(feature.Booked(eventId), ReservationOutcomeKind.Missed);
        }

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(
            $"/api/reservations/outcomes?perPage={ReservationOutcomeQuery.MostPerPage + 1}");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(ReservationOutcomeQuery.MostPerPage, data.GetProperty("items").GetArrayLength());
        Assert.Equal(ReservationOutcomeQuery.MostPerPage, data.GetProperty("perPage").GetInt32());
        Assert.Equal(2, data.GetProperty("lastPage").GetInt32());
    }

    [Fact]
    public async Task OnlyTheKindsAskedForComeBack()
    {
        await using var feature = new ReservationFeature();
        feature.Recorded(feature.Booked(4001), ReservationOutcomeKind.Competing, recordedInstead: [Guid.NewGuid()]);
        feature.Recorded(feature.Booked(4002), ReservationOutcomeKind.Missed);
        feature.Recorded(feature.Booked(4003), ReservationOutcomeKind.TuneFailure, tuneFailure: TuneFailureKind.NoData);
        feature.Recorded(
            feature.Booked(4004),
            ReservationOutcomeKind.RecordingFailure,
            recordingOutcome: RecordingOutcome.Failed);

        (_, JsonElement body) = await feature.GetAsync(
            "/api/reservations/outcomes?kind=competing&kind=recordingFailure");

        Assert.Equal(
            ["competing", "recordingFailure"],
            body.GetProperty("data").GetProperty("items").EnumerateArray().Select(Kind).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task OnlyTheChannelsAskedForComeBack()
    {
        await using var feature = new ReservationFeature();
        feature.Recorded(feature.Booked(4001, serviceId: 1024), ReservationOutcomeKind.Missed);
        feature.Recorded(feature.Booked(4002, serviceId: 1032), ReservationOutcomeKind.Missed);

        (_, JsonElement body) = await feature.GetAsync(
            $"/api/reservations/outcomes?channel={ReservationFeature.Network}-1032");
        JsonElement items = body.GetProperty("data").GetProperty("items");

        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(1032, items[0].GetProperty("programme").GetProperty("serviceId").GetInt32());
    }

    [Fact]
    public async Task OnlyWhatCameOfTheRuleAskedForComesBack()
    {
        await using var feature = new ReservationFeature();
        RuleId wanted = RuleId.New();
        feature.Recorded(feature.Booked(4001, ruleId: wanted), ReservationOutcomeKind.Missed);
        feature.Recorded(feature.Booked(4002, ruleId: RuleId.New()), ReservationOutcomeKind.Missed);
        feature.Recorded(feature.Booked(4003), ReservationOutcomeKind.Missed);

        (_, JsonElement body) = await feature.GetAsync($"/api/reservations/outcomes?rule={wanted.Value}");
        JsonElement items = body.GetProperty("data").GetProperty("items");

        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(wanted.Value, items[0].GetProperty("ruleId").GetGuid());
    }

    [Fact]
    public async Task ASpanReadsWhenTheOutcomeWasWrittenDownRatherThanWhenTheProgrammeWasOn()
    {
        await using var feature = new ReservationFeature();
        feature.Recorded(feature.Booked(4001, startsAt: Noon.AddHours(2)), ReservationOutcomeKind.Missed, Noon.AddHours(4));
        feature.Recorded(feature.Booked(4002, startsAt: Noon.AddHours(2)), ReservationOutcomeKind.Missed, Noon.AddHours(20));

        (_, JsonElement body) = await feature.GetAsync(
            "/api/reservations/outcomes?from=2026-08-24T16:00:00Z&to=2026-08-24T17:00:00Z");

        Assert.Equal(1, body.GetProperty("data").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task AnOutcomeIsAnsweredAsTheLedgerWroteItDown_BR_RD_012()
    {
        await using var feature = new ReservationFeature();
        Guid[] instead = [Guid.NewGuid(), Guid.NewGuid()];
        RuleId rule = RuleId.New();
        Reservation lost = feature.Booked(4001, ruleId: rule, priority: 30, name: "Harbour report");
        ReservationOutcome written = feature.Recorded(
            lost,
            ReservationOutcomeKind.Competing,
            Noon.AddHours(3),
            recordedInstead: instead);

        (_, JsonElement body) = await feature.GetAsync("/api/reservations/outcomes");
        JsonElement item = Assert.Single(body.GetProperty("data").GetProperty("items").EnumerateArray());

        Assert.Equal(written.Id.Value, item.GetProperty("id").GetGuid());
        Assert.Equal(lost.Id.Value, item.GetProperty("reservationId").GetGuid());
        Assert.Equal("competing", Kind(item));
        Assert.Equal(instead, item.GetProperty("recordedInstead").EnumerateArray().Select(one => one.GetGuid()));
        Assert.Equal(rule.Value, item.GetProperty("ruleId").GetGuid());
        Assert.Equal(30, item.GetProperty("priority").GetInt32());
        Assert.Equal(Noon.AddHours(3), item.GetProperty("occurredAt").GetDateTime());
        Assert.Equal(lost.EffectiveStartAt, item.GetProperty("effectiveStartAt").GetDateTime());
        Assert.Equal(lost.EffectiveEndAt, item.GetProperty("effectiveEndAt").GetDateTime());
        Assert.Equal("Harbour report", item.GetProperty("programme").GetProperty("name").GetString());
        Assert.Equal(
            $"{ReservationFeature.Network}-1024-4001",
            item.GetProperty("programme").GetProperty("id").GetString());
    }

    [Fact]
    public async Task WhatTheLedgerDidNotWriteDownIsAnsweredAsNothingRatherThanGuessed_BR_RD_012()
    {
        await using var feature = new ReservationFeature();
        feature.Recorded(
            feature.Booked(4001),
            ReservationOutcomeKind.RecordingFailure,
            recordingOutcome: RecordingOutcome.Failed);
        feature.Recorded(
            feature.Booked(4002),
            ReservationOutcomeKind.TuneFailure,
            tuneFailure: TuneFailureKind.StreamMismatch);

        (_, JsonElement body) = await feature.GetAsync("/api/reservations/outcomes?kind=recordingFailure");
        JsonElement failed = Assert.Single(body.GetProperty("data").GetProperty("items").EnumerateArray());
        (_, JsonElement tuned) = await feature.GetAsync("/api/reservations/outcomes?kind=tuneFailure");
        JsonElement refused = Assert.Single(tuned.GetProperty("data").GetProperty("items").EnumerateArray());

        Assert.Equal(JsonValueKind.Null, failed.GetProperty("tuneFailure").ValueKind);
        Assert.Equal("failed", failed.GetProperty("recordingOutcome").GetString());
        Assert.Empty(failed.GetProperty("recordedInstead").EnumerateArray());
        Assert.Equal("streamMismatch", refused.GetProperty("tuneFailure").GetString());
        Assert.Equal(JsonValueKind.Null, refused.GetProperty("recordingOutcome").ValueKind);
    }

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

    [Theory]
    [InlineData("/api/reservations/outcomes")]
    [InlineData("/api/reservations/health")]
    public async Task NeitherSurfaceIsReachedWithoutASession_BR_RA_002(string path)
    {
        await using var feature = new ReservationFeature();
        feature.Client.DefaultRequestHeaders.Authorization = null;

        (HttpStatusCode status, _) = await feature.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    private static string Kind(JsonElement item) => item.GetProperty("kind").GetString()!;
}
