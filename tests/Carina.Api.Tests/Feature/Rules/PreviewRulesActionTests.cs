using System.Net;
using System.Text.Json;

using Carina.Domain.Programmes;

namespace Carina.Api.Tests.Feature.Rules;

[Trait("Category", "Feature")]
public sealed class PreviewRulesActionTests
{
    [Fact]
    public async Task APreviewSaysWhatADraftWouldTakeWithoutWritingAnything()
    {
        await using var feature = new RuleFeature();
        feature.Announced(1, "hill walking");
        feature.Announced(2, "river fishing");

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            "/api/rules/preview",
            new { query = "keyword=hill" });

        Assert.Equal(HttpStatusCode.OK, status);
        JsonElement data = body.GetProperty("data");
        Assert.Equal(1, data.GetProperty("matched").GetInt32());
        Assert.Equal(1, data.GetProperty("making").GetInt32());
        Assert.Equal(
            ["hill walking"],
            data.GetProperty("takes").EnumerateArray()
                .Select(take => take.GetProperty("name").GetString() ?? string.Empty)
                .ToArray());
        Assert.Empty(feature.Rules.Rules);
        Assert.Empty(feature.Reservations.Held);
    }

    [Fact]
    public async Task APreviewTakesTheProgrammesByTheMarksTheyCarryRatherThanByTheLettersInTheirNames()
    {
        await using var feature = new RuleFeature();
        feature.Announced(1, "\U0001F21Fhill walking");
        feature.Announced(2, "新 hill walking [新]");
        feature.Announced(3, "\U0001F21F\U0001F21Ehill walking");
        feature.Announced(4, "hill walking 再");

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            "/api/rules/preview",
            new { query = "mark=New&excludeMark=Rerun" });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            ["\U0001F21Fhill walking"],
            body.GetProperty("data").GetProperty("takes").EnumerateArray()
                .Select(take => take.GetProperty("name").GetString() ?? string.Empty)
                .ToArray());
    }

    [Fact]
    public async Task APreviewCountsWhatIsCarriedAsAShadowOutRatherThanTakingIt()
    {
        await using var feature = new RuleFeature();
        DateTime opens = RuleFeature.Noon.AddHours(3);
        feature.Announced(1, "hill walking", startsAt: opens);
        feature.Announced(2, "hill walking", RuleFeature.Alongside, opens, shadow: true);

        (_, JsonElement body) = await feature.PostAsync("/api/rules/preview", new { query = "keyword=hill" });

        Assert.Equal(1, body.GetProperty("data").GetProperty("matched").GetInt32());
        Assert.Equal(1, body.GetProperty("data").GetProperty("excludedAsShadows").GetInt32());
    }

    [Fact]
    public async Task BrRd010APreviewCountsAListingAMovedBroadcastSuppressesOutAndTakesThePrimary()
    {
        await using var feature = new RuleFeature();
        DateTime opens = RuleFeature.Noon.AddHours(3);
        feature.Announced(
            1,
            "hill walking",
            startsAt: opens,
            related: [new RelatedProgramme(RuleFeature.Network, RuleFeature.Alongside, 2, RelationKind.Moved)]);
        feature.Announced(2, "hill walking", RuleFeature.Alongside, opens, running: ProgrammeRunning.Running);

        (_, JsonElement body) = await feature.PostAsync("/api/rules/preview", new { query = "keyword=hill" });
        JsonElement data = body.GetProperty("data");

        Assert.Equal(1, data.GetProperty("matched").GetInt32());
        Assert.Equal(1, data.GetProperty("excludedAsMoved").GetInt32());
        Assert.Equal(0, data.GetProperty("excludedAsShadows").GetInt32());
        Assert.Equal(
            $"{RuleFeature.Network}-{RuleFeature.Alongside}-2",
            data.GetProperty("takes").EnumerateArray().Single().GetProperty("programme").GetString());
    }

    [Fact]
    public async Task APreviewWithNothingCarriedAsAShadowCountsNoneOut()
    {
        await using var feature = new RuleFeature();
        DateTime opens = RuleFeature.Noon.AddHours(3);
        feature.Announced(1, "hill walking", startsAt: opens);
        feature.Announced(2, "hill climbing", RuleFeature.Alongside, opens);

        (_, JsonElement body) = await feature.PostAsync("/api/rules/preview", new { query = "keyword=hill" });

        Assert.Equal(2, body.GetProperty("data").GetProperty("matched").GetInt32());
        Assert.Equal(0, body.GetProperty("data").GetProperty("excludedAsShadows").GetInt32());
    }

    [Fact]
    public async Task AnUnsavedDraftClashesWithWhatAlreadyStandsWhenThereIsOneSeat()
    {
        await using var feature = new RuleFeature(seats: 1);
        DateTime opens = RuleFeature.Noon.AddHours(3);
        feature.Announced(1, "hill walking", startsAt: opens);
        feature.Booked(feature.Announced(2, "a booking", RuleFeature.Alongside, opens));

        (_, JsonElement body) = await feature.PostAsync("/api/rules/preview", new { query = "keyword=hill" });

        Assert.Equal(1, body.GetProperty("data").GetProperty("making").GetInt32());
        Assert.Equal(1, body.GetProperty("data").GetProperty("contendedAltogether").GetInt32());
    }

    [Fact]
    public async Task TheSameDraftClashesWithNothingWhenThereIsASeatForItAsWell()
    {
        await using var feature = new RuleFeature(seats: 2);
        DateTime opens = RuleFeature.Noon.AddHours(3);
        feature.Announced(1, "hill walking", startsAt: opens);
        feature.Booked(feature.Announced(2, "a booking", RuleFeature.Alongside, opens));

        (_, JsonElement body) = await feature.PostAsync("/api/rules/preview", new { query = "keyword=hill" });

        Assert.Equal(1, body.GetProperty("data").GetProperty("making").GetInt32());
        Assert.Equal(0, body.GetProperty("data").GetProperty("contendedAltogether").GetInt32());
    }

    [Fact]
    public async Task APreviewIsRefusedRatherThanGuessedWhenTheTunersCannotBeCounted()
    {
        await using var feature = new RuleFeature();
        feature.Announced(1, "hill walking");
        feature.Seating.Capacity = null;

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            "/api/rules/preview",
            new { query = "keyword=hill" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.False(body.GetProperty("status").GetBoolean());
    }

    [Fact]
    public async Task APreviewIsAnsweredWhenTheTunersCanBeCounted()
    {
        await using var feature = new RuleFeature();
        feature.Announced(1, "hill walking");

        (HttpStatusCode status, _) = await feature.PostAsync(
            "/api/rules/preview",
            new { query = "keyword=hill" });

        Assert.Equal(HttpStatusCode.OK, status);
    }

    [Fact]
    public async Task APreviewOfADraftThatNarrowsNothingIsRefused()
    {
        await using var feature = new RuleFeature();

        (HttpStatusCode status, _) = await feature.PostAsync("/api/rules/preview", new { query = "sort=Name" });

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }
}
