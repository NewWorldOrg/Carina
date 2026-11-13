using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;

using Microsoft.EntityFrameworkCore;

namespace Carina.Api.Tests.Feature.Reservations;

[Trait("Category", "Feature")]
public sealed class ListReservationsActionTests
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
    public async Task AReservationThatHasBeenRecordedIsSaidToBeCompleteAndNothingBesideItSaysScheduled()
    {
        await using var feature = new ReservationFeature();
        feature.Booked(
            4001,
            startsAt: Noon.AddHours(-3),
            startedAt: Noon.AddHours(-3),
            outcome: RecordingOutcome.Complete);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/reservations");
        JsonElement only = body.GetProperty("data").GetProperty("items")[0];

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("complete", Standing(only));
        Assert.False(
            only.TryGetProperty("state", out JsonElement raw),
            $"A reservation answers with one state, and this one also said state={raw}.");
    }

    private static string Standing(JsonElement reservation)
        => reservation.GetProperty("standing").GetString()!;

    private static IEnumerable<int> Priorities(JsonElement body)
        => body.GetProperty("data").GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("priority").GetInt32());
}
