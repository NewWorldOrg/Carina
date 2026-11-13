using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;

namespace Carina.Api.Tests.Feature.DataBroadcast;

[Trait("Category", "Feature")]
public sealed class PutDataBroadcastSettingsActionTests
{
    [Fact(DisplayName = "BR-BD-016: registering the postal code and the prefecture answers when they changed, and is read back")]
    public async Task RegisteringIsReadBack()
    {
        await using var feature = new DataBroadcastSettingsFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync("""{"postalCode":"0000000","prefecture":13}""");
        (_, JsonElement read) = await feature.GetAsync();
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("0000000", data.GetProperty("postalCode").GetString());
        Assert.Equal(13, data.GetProperty("prefecture").GetInt32());
        Assert.Equal(DataBroadcastSettingsFeature.Noon, data.GetProperty("changedAt").GetDateTime());
        Assert.Equal(data.GetRawText(), read.GetProperty("data").GetRawText());
        Assert.Equal((1, 1), (feature.Settings.Saves, feature.Settings.Turns));
    }

    [Theory(DisplayName = "BR-BD-016: a postal code sent empty or null, and a prefecture sent null, take back what was registered")]
    [InlineData("""{"postalCode":"","prefecture":null}""")]
    [InlineData("""{"postalCode":null,"prefecture":null}""")]
    public async Task EmptyOrNullTakesBackWhatWasRegistered(string json)
    {
        await using var feature = new DataBroadcastSettingsFeature();
        await feature.PutAsync("""{"postalCode":"0000000","prefecture":0}""");
        feature.Clock.Now = DataBroadcastSettingsFeature.Noon.AddMinutes(5);

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync(json);
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("postalCode").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("prefecture").ValueKind);
        Assert.Equal(DataBroadcastSettingsFeature.Noon.AddMinutes(5), data.GetProperty("changedAt").GetDateTime());
    }

    [Fact(DisplayName = "BR-BD-016: sending what stands writes nothing and keeps when it last changed")]
    public async Task SendingWhatStandsWritesNothing()
    {
        await using var feature = new DataBroadcastSettingsFeature();
        await feature.PutAsync("""{"postalCode":"0000000","prefecture":49}""");
        feature.Clock.Now = DataBroadcastSettingsFeature.Noon.AddMinutes(10);

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync("""{"postalCode":"0000000","prefecture":49}""");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(DataBroadcastSettingsFeature.Noon, body.GetProperty("data").GetProperty("changedAt").GetDateTime());
        Assert.Equal((1, 2), (feature.Settings.Saves, feature.Settings.Turns));
    }

    [Theory(DisplayName = "BR-BD-016: a postal code that is not seven digits is refused naming it, and nothing is written")]
    [InlineData("""{"postalCode":"000000","prefecture":null}""")]
    [InlineData("""{"postalCode":"00000000","prefecture":null}""")]
    [InlineData("""{"postalCode":"000-0000","prefecture":null}""")]
    [InlineData("""{"postalCode":"０００００００","prefecture":null}""")]
    [InlineData("""{"postalCode":" ","prefecture":13}""")]
    public async Task APostalCodeThatIsNotSevenDigitsIsRefused(string json)
    {
        await using var feature = new DataBroadcastSettingsFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync(json);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.StartsWith("postalCode", body.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").ValueKind);
        Assert.Equal((0, 0), (feature.Settings.Saves, feature.Settings.Turns));
    }

    [Theory(DisplayName = "BR-BD-016: a prefecture outside 0 to 49 is refused naming it, and nothing is written")]
    [InlineData(-1)]
    [InlineData(50)]
    [InlineData(255)]
    public async Task APrefectureOutsideTheDesignationIsRefused(int prefecture)
    {
        await using var feature = new DataBroadcastSettingsFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync($$"""{"postalCode":"0000000","prefecture":{{prefecture}}}""");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.StartsWith("prefecture", body.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Equal((0, 0), (feature.Settings.Saves, feature.Settings.Turns));
    }

    [Theory(DisplayName = "BR-BD-016: a change leaving out either value, or naming anything else, is refused, and nothing is written")]
    [InlineData("{}", "the body")]
    [InlineData("""{"postalCode":"0000000"}""", "the body")]
    [InlineData("""{"prefecture":13}""", "the body")]
    [InlineData("null", "the body")]
    [InlineData("""{"postalCode":"0000000","prefecture":13,"regionCode":0}""", "regionCode")]
    [InlineData("""{"postalCode":0,"prefecture":13}""", "postalCode")]
    [InlineData("""{"postalCode":"0000000","prefecture":true}""", "prefecture")]
    public async Task AChangeLeavingOutAValueOrNamingAnythingElseIsRefused(string json, string named)
    {
        await using var feature = new DataBroadcastSettingsFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync(json);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains(named, body.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Equal(0, feature.Settings.Saves);
    }

    [Fact(DisplayName = "BR-BA-001: the settings are not changed without a session")]
    public async Task TheSettingsAreNotChangedWithoutASession()
    {
        await using DataBroadcastSettingsFeature feature = new();
        using HttpClient anonymous = feature.Built.WithTestScheme().CreateClient();
        using StringContent content = new("""{"postalCode":"0000000","prefecture":13}""", System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage changed = await anonymous.PutAsync(new Uri(DataBroadcastSettingsFeature.Path, UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.Unauthorized, changed.StatusCode);
        Assert.Equal(0, feature.Settings.Saves);
    }
}
