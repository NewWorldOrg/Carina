using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Carina.Api.Tests.Feature.DataBroadcast;

[Trait("Category", "Feature")]
public sealed class GetDataBroadcastSettingsActionTests
{
    [Fact(DisplayName = "BR-BD-016: with no row held neither the postal code nor the prefecture is registered, and nobody has changed them")]
    public async Task WithNoRowHeldNothingIsRegistered()
    {
        await using var feature = new DataBroadcastSettingsFeature();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync();
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["postalCode", "prefecture", "changedAt"], data.EnumerateObject().Select(field => field.Name).ToArray());
        Assert.All(data.EnumerateObject(), field => Assert.Equal(JsonValueKind.Null, field.Value.ValueKind));
        Assert.Equal(0, feature.Settings.Saves);
    }

    [Fact(DisplayName = "BR-BA-001: the settings are not read without a session")]
    public async Task TheSettingsAreNotReadWithoutASession()
    {
        await using DataBroadcastSettingsFeature feature = new();
        using HttpClient anonymous = feature.Built.WithTestScheme().CreateClient();

        using HttpResponseMessage read = await anonymous.GetAsync(new Uri(DataBroadcastSettingsFeature.Path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.Equal(0, feature.Settings.Saves);
    }
}
