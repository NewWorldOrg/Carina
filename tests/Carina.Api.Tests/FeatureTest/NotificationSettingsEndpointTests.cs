using System.Net;
using System.Text.Json;

using Carina.Domain.Notifications;

namespace Carina.Api.Tests.FeatureTest;

public sealed class NotificationSettingsEndpointTests
{
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

    [Theory(DisplayName = "BR-NT-005: The lead is kept when it is one of the minutes offered, and is read back")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task ALeadThatIsOfferedIsKept(int minutes)
    {
        await using NotificationFeature feature = new();
        feature.Clock.Now = NotificationFeature.Noon.AddHours(1);

        (HttpStatusCode put, JsonElement changed) = await feature.PutAsync(NotificationFeature.Settings, new { notifyLeadMinutes = minutes });
        (HttpStatusCode get, JsonElement read) = await feature.GetAsync(NotificationFeature.Settings);

        Assert.Equal(HttpStatusCode.OK, put);
        Assert.Equal(minutes, changed.GetProperty("data").GetProperty("notifyLeadMinutes").GetInt32());
        Assert.Equal(HttpStatusCode.OK, get);
        Assert.Equal(minutes, read.GetProperty("data").GetProperty("notifyLeadMinutes").GetInt32());
        Assert.Equal(minutes, feature.HeldSettings.Settings!.NotifyLeadMinutes);
        Assert.Equal(NotificationFeature.Noon.AddHours(1), feature.HeldSettings.Settings!.ChangedAt);
    }

    [Theory(DisplayName = "BR-NT-005: A lead that is not one of the minutes offered is refused and what is held stays")]
    [InlineData(2)]
    [InlineData(-1)]
    [InlineData(15)]
    public async Task ALeadThatIsNotOfferedIsRefused(int minutes)
    {
        await using NotificationFeature feature = new();
        feature.HeldSettings.Settings = NotificationSettings.Set(5, NotificationFeature.Noon);

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync(NotificationFeature.Settings, new { notifyLeadMinutes = minutes });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.False(body.GetProperty("status").GetBoolean());
        Assert.Equal(5, feature.HeldSettings.Settings!.NotifyLeadMinutes);
    }

    [Fact(DisplayName = "BR-NT-005: Settings that name no lead are refused")]
    public async Task SettingsThatNameNoLeadAreRefused()
    {
        await using NotificationFeature feature = new();

        (HttpStatusCode status, JsonElement _) = await feature.PutAsync(NotificationFeature.Settings, new { });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Null(feature.HeldSettings.Settings);
    }
}
