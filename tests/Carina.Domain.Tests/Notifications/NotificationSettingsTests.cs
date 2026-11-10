using Carina.Domain.Notifications;

namespace Carina.Domain.Tests.Notifications;

public sealed class NotificationSettingsTests
{
    private static readonly DateTime At = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    [Theory(DisplayName = "BR-NT-005: a notice of a programme is sent 0, 1, 3, 5 or 10 minutes before it begins")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    public void TheLeadIsOneOfTheMinutesOffered(int minutes)
    {
        Assert.Equal(minutes, NotificationSettings.Set(minutes, At).NotifyLeadMinutes);
    }

    [Theory(DisplayName = "BR-NT-005: no other lead is kept")]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(15)]
    public void NoOtherLeadIsKept(int minutes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NotificationSettings.Set(minutes, At));
    }

    [Fact(DisplayName = "BR-NT-005: the lead is 3 minutes while no row is held")]
    public void TheLeadIsThreeMinutesWhileNoRowIsHeld()
    {
        Assert.Equal(3, NotificationSettings.LeadMinutesOver(null));
        Assert.Equal(10, NotificationSettings.LeadMinutesOver(NotificationSettings.Set(10, At)));
    }

    [Fact]
    public void TheSettingsAreOneRow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NotificationSettings.Rehydrate(2, 3, At));
    }
}
