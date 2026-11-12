namespace Carina.Api.Tests.Feature.Notifications;

[Trait("Category", "Feature")]
public sealed class ListNotificationRulesActionTests
{
    [Theory(DisplayName = "BR-NA-001: A caller who has not signed in is refused by every endpoint of the notifications")]
    [InlineData("GET", NotificationFeature.Rules)]
    public async Task ACallerWhoHasNotSignedInIsRefused(string method, string path)
        => await NotificationGuard.ACallerWhoHasNotSignedInIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A read from another origin is left alone")]
    [InlineData("GET", NotificationFeature.Rules)]
    public async Task AReadFromAnotherOriginIsLeftAlone(string method, string path)
        => await NotificationGuard.AReadFromAnotherOriginIsLeftAloneAsync(method, path);
}
