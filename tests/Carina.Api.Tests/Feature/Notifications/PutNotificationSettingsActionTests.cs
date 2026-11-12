using System.Net;
using System.Text.Json;

using Carina.Domain.Notifications;

namespace Carina.Api.Tests.Feature.Notifications;

[Trait("Category", "Feature")]
public sealed class PutNotificationSettingsActionTests
{
    [Theory(DisplayName = "BR-NA-001: A caller who has not signed in is refused by every endpoint of the notifications")]
    [InlineData("PUT", NotificationFeature.Settings)]
    public async Task ACallerWhoHasNotSignedInIsRefused(string method, string path)
        => await NotificationGuard.ACallerWhoHasNotSignedInIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that names no origin is refused")]
    [InlineData("PUT", NotificationFeature.Settings)]
    public async Task AWriteNamingNoOriginIsRefused(string method, string path)
        => await NotificationGuard.AWriteNamingNoOriginIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that names another origin is refused")]
    [InlineData("PUT", NotificationFeature.Settings)]
    public async Task AWriteNamingAnotherOriginIsRefused(string method, string path)
        => await NotificationGuard.AWriteNamingAnotherOriginIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that carries a form and not JSON is refused")]
    [InlineData("PUT", NotificationFeature.Settings)]
    public async Task AWriteCarryingAFormIsRefused(string method, string path)
        => await NotificationGuard.AWriteCarryingAFormIsRefusedAsync(method, path);

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
