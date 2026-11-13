using System.Net;
using System.Text.Json;

using Carina.Domain.Reservations;
using Carina.Domain.Rules;

namespace Carina.Api.Tests.Feature.Rules;

[Trait("Category", "Feature")]
public sealed class ReplaceRuleActionTests
{
    [Fact(DisplayName = "rewriting a rule carries what the rewrite asked about encoding")]
    public async Task RewritingARuleCarriesWhatItAskedAboutEncoding()
    {
        await using var feature = new RuleFeature();
        Rule written = feature.Written(name: "hills", query: "keyword=hill");

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync(
            $"/api/rules/{written.Id.Value}",
            new { name = "hills", query = "keyword=hill", encodeWhenRecorded = false });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body.GetProperty("data").GetProperty("encodeWhenRecorded").GetBoolean());
        Assert.False(feature.Rules.Rules[0].EncodeWhenRecorded);
    }

    [Fact]
    public async Task ARuleIsRewrittenAndTheBellIsRung()
    {
        await using var feature = new RuleFeature();
        Rule written = feature.Written(name: "hills", query: "keyword=hill");

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync(
            $"/api/rules/{written.Id.Value}",
            new { name = "rivers", query = "keyword=river", priority = 20 });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("rivers", body.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal("keyword=river", body.GetProperty("data").GetProperty("query").GetString());
        Assert.Equal("rivers", feature.Rules.Rules[0].Name);
        Assert.Equal([RecalculationTrigger.RulesChanged], feature.Notices.Nudged);
    }

    [Fact]
    public async Task ARewriteThatNarrowsNothingLeavesTheRuleAsItWas()
    {
        await using var feature = new RuleFeature();
        Rule written = feature.Written(name: "hills", query: "keyword=hill");

        (HttpStatusCode status, _) = await feature.PutAsync(
            $"/api/rules/{written.Id.Value}",
            new { name = "everything", query = "sort=Name" });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("hills", feature.Rules.Rules[0].Name);
        Assert.Empty(feature.Notices.Nudged);
    }
}
