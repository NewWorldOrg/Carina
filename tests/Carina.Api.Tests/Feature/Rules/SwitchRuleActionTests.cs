using System.Net;
using System.Text.Json;

using Carina.Domain.Rules;

namespace Carina.Api.Tests.Feature.Rules;

[Trait("Category", "Feature")]
public sealed class SwitchRuleActionTests
{
    [Fact]
    public async Task SwitchingARuleOffPullsWhatItMadeBackAndSaysHowMuchCameBack()
    {
        await using var feature = new RuleFeature();
        feature.Collected();
        Rule written = feature.Written();
        feature.Booked(feature.Announced(1), written.Id);

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync(
            $"/api/rules/{written.Id.Value}/enabled",
            new { enabled = false });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body.GetProperty("data").GetProperty("rule").GetProperty("enabled").GetBoolean());
        Assert.Equal(1, body.GetProperty("data").GetProperty("withdrawn").GetInt32());
        Assert.Empty(feature.Reservations.Held);
    }

    [Fact]
    public async Task SwitchingARuleOnLeavesTheReservationsAloneAndSaysNothingCameBack()
    {
        await using var feature = new RuleFeature();
        feature.Collected();
        Rule written = feature.Written(enabled: false);
        feature.Booked(feature.Announced(1), written.Id);

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync(
            $"/api/rules/{written.Id.Value}/enabled",
            new { enabled = true });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("data").GetProperty("rule").GetProperty("enabled").GetBoolean());
        Assert.Equal(0, body.GetProperty("data").GetProperty("withdrawn").GetInt32());
        Assert.Single(feature.Reservations.Held);
    }

    [Fact]
    public async Task ASwitchThatSaysNeitherOnNorOffIsRefused()
    {
        await using var feature = new RuleFeature();
        Rule written = feature.Written();

        (HttpStatusCode status, _) = await feature.PatchAsync(
            $"/api/rules/{written.Id.Value}/enabled",
            new { });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(feature.Rules.Rules[0].Enabled);
    }
}
