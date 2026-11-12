using System.Net;
using System.Text.Json;

using Carina.Domain.Notifications;

namespace Carina.Api.Tests.Feature.Notifications;

[Trait("Category", "Feature")]
public sealed class RemoveNotificationRuleActionTests
{
    [Theory(DisplayName = "BR-NA-001: A caller who has not signed in is refused by every endpoint of the notifications")]
    [InlineData("DELETE", NotificationGuard.OneRule)]
    public async Task ACallerWhoHasNotSignedInIsRefused(string method, string path)
        => await NotificationGuard.ACallerWhoHasNotSignedInIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that names no origin is refused")]
    [InlineData("DELETE", NotificationGuard.OneRule)]
    public async Task AWriteNamingNoOriginIsRefused(string method, string path)
        => await NotificationGuard.AWriteNamingNoOriginIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that names another origin is refused")]
    [InlineData("DELETE", NotificationGuard.OneRule)]
    public async Task AWriteNamingAnotherOriginIsRefused(string method, string path)
        => await NotificationGuard.AWriteNamingAnotherOriginIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that carries a form and not JSON is refused")]
    [InlineData("DELETE", NotificationGuard.OneRule)]
    public async Task AWriteCarryingAFormIsRefused(string method, string path)
        => await NotificationGuard.AWriteCarryingAFormIsRefusedAsync(method, path);

    [Fact(DisplayName = "A set of rules is removed once, and the destinations it told stay")]
    public async Task ARuleIsRemovedOnce()
    {
        await using NotificationFeature feature = new();
        await feature.RegisteredAsync();
        Guid usual = Assert.Single(feature.HeldRules.Rules).Id.Value;

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync(NotificationFeature.RuleOf(usual));
        (HttpStatusCode again, _) = await feature.DeleteAsync(NotificationFeature.RuleOf(usual));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(usual, body.GetProperty("data").GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, again);
        Assert.Empty(feature.HeldRules.Rules);
        Assert.Single(feature.HeldDestinations.Destinations);
    }
}
