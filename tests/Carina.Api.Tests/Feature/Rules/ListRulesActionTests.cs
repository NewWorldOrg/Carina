using System.Net;
using System.Text.Json;

namespace Carina.Api.Tests.Feature.Rules;

[Trait("Category", "Feature")]
public sealed class ListRulesActionTests
{
    [Fact]
    public async Task TheRulesAreListedInTheOrderTheyTakeProgrammes()
    {
        await using var feature = new RuleFeature();
        feature.Written(name: "behind", priority: 10, identifier: 1);
        feature.Written(name: "ahead", priority: 90, identifier: 2);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/rules");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            ["ahead", "behind"],
            body.GetProperty("data").GetProperty("rules").EnumerateArray()
                .Select(rule => rule.GetProperty("name").GetString() ?? string.Empty)
                .ToArray());
        Assert.Equal(2, body.GetProperty("data").GetProperty("total").GetInt32());
    }
}
