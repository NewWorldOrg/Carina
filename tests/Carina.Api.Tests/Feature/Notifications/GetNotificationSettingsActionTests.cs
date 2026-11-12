using System.Net;
using System.Text.Json;

using Carina.Domain.Notifications;

namespace Carina.Api.Tests.Feature.Notifications;

[Trait("Category", "Feature")]
public sealed class GetNotificationSettingsActionTests
{
    [Theory(DisplayName = "BR-NA-001: A caller who has not signed in is refused by every endpoint of the notifications")]
    [InlineData("GET", NotificationFeature.Settings)]
    public async Task ACallerWhoHasNotSignedInIsRefused(string method, string path)
        => await NotificationGuard.ACallerWhoHasNotSignedInIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A read from another origin is left alone")]
    [InlineData("GET", NotificationFeature.Settings)]
    public async Task AReadFromAnotherOriginIsLeftAlone(string method, string path)
        => await NotificationGuard.AReadFromAnotherOriginIsLeftAloneAsync(method, path);

    [Fact(DisplayName = "BR-NT-005: Until somebody changes it a notice of a programme is sent three minutes before, and the settings say no public origin is known")]
    public async Task TheSettingsStartAtThreeMinutesWithNoPublicOrigin()
    {
        await using NotificationFeature feature = new();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(NotificationFeature.Settings);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(3, body.GetProperty("data").GetProperty("notifyLeadMinutes").GetInt32());
        Assert.False(body.GetProperty("data").GetProperty("publicOriginKnown").GetBoolean());
        Assert.Null(feature.HeldSettings.Settings);
    }

    [Fact(DisplayName = "D-7: The settings say a public origin is known when one is configured, and never spell it out")]
    public async Task TheSettingsSayWhetherAPublicOriginIsKnown()
    {
        await using NotificationFeature feature = new(publicOrigin: "https://tv.example.test");

        using HttpResponseMessage response = await feature.Client.GetAsync(new Uri(NotificationFeature.Settings, UriKind.Relative));
        string text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"publicOriginKnown\":true", text, StringComparison.Ordinal);
        Assert.DoesNotContain("tv.example.test", text, StringComparison.Ordinal);
    }
}
