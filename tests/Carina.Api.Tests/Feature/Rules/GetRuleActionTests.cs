using System.Net;
using System.Text.Json;

using Carina.Domain.Rules;

namespace Carina.Api.Tests.Feature.Rules;

[Trait("Category", "Feature")]
public sealed class GetRuleActionTests
{
    [Fact]
    public async Task ARuleIsReadBackByItsOwnName()
    {
        await using var feature = new RuleFeature();
        Rule written = feature.Written(name: "hills");

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"/api/rules/{written.Id.Value}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("hills", body.GetProperty("data").GetProperty("name").GetString());
    }

    [Fact]
    public async Task ARuleThatIsNotThereIsAnsweredAsNotThere()
    {
        await using var feature = new RuleFeature();

        (HttpStatusCode status, _) = await feature.GetAsync($"/api/rules/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, status);
    }
}
