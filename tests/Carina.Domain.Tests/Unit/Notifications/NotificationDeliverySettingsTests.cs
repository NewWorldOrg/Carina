using Carina.Domain.Notifications;

namespace Carina.Domain.Tests.Unit.Notifications;

[Trait("Category", "Unit")]
public sealed class NotificationDeliverySettingsTests
{
    [Fact(DisplayName = "BR-NC-002: a destination is sent thirty notices in sixty seconds at most")]
    public void ADestinationIsSentThirtyNoticesInSixtySecondsAtMost()
    {
        NotificationDeliverySettings settings = new();

        Assert.Equal(30, settings.MostInAWindow);
        Assert.Equal(TimeSpan.FromSeconds(60), settings.Window);
    }

    [Fact(DisplayName = "BR-NS-001: a notice is too old to send an hour after what it tells of, and is forgotten after fourteen days")]
    public void ANoticeIsTooOldAfterAnHourAndForgottenAfterFourteenDays()
    {
        NotificationDeliverySettings settings = new();

        Assert.Equal(TimeSpan.FromHours(1), settings.TooOldAfter);
        Assert.Equal(TimeSpan.FromDays(14), settings.Keeps);
    }

    [Fact(DisplayName = "BR-NC-002: a destination is sent a trial once in sixty seconds, and all of them five")]
    public void TrialsAreCountedPerDestinationAndOverAll()
    {
        NotificationDeliverySettings settings = new();

        Assert.Equal(1, settings.MostTrialsOfOneInAWindow);
        Assert.Equal(5, settings.MostTrialsInAWindow);
    }
}
