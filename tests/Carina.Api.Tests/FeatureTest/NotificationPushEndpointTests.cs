using Carina.Api.Tests.Feature.Notifications;
using Carina.Infrastructure.Notifications;
using Carina.TestSupport;

namespace Carina.Api.Tests.FeatureTest;

public sealed class NotificationPushEndpointTests
{
    [Theory(DisplayName = "BR-NS-002: A push service that says the subscription is gone turns the browser's row off, and one that fails is tried again later with the row left on")]
    [InlineData(404, false, 0, 1)]
    [InlineData(410, false, 0, 1)]
    [InlineData(500, true, 1, 0)]
    [InlineData(429, true, 1, 0)]
    public async Task APushServiceThatSaysTheSubscriptionIsGoneTurnsTheRowOff(int answered, bool staysOn, int failed, int givenUp)
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver browser = new();
        Guid id = await feature.SubscribedAsync(browser);
        feature.End.Status = answered;
        feature.Waiting(id);

        NotificationDeliveries passed = await feature.PassAsync();

        Assert.Equal(failed, passed.Failed);
        Assert.Equal(givenUp, passed.GivenUp);
        Assert.Single(feature.End.Heard);
        Assert.Equal(staysOn, Assert.Single(feature.HeldDestinations.Destinations).Enabled);
    }
}
