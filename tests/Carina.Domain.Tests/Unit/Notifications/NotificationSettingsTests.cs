using Carina.Domain.Notifications;

namespace Carina.Domain.Tests.Unit.Notifications;

[Trait("Category", "Unit")]
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

    [Theory(DisplayName = "BR-NT-005: only the minutes offered are offered, and no minutes at all are not")]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(5, true)]
    [InlineData(10, true)]
    [InlineData(-1, false)]
    [InlineData(2, false)]
    [InlineData(15, false)]
    [InlineData(null, false)]
    public void OnlyTheMinutesOfferedAreOffered(int? minutes, bool offered)
    {
        Assert.Equal(offered, NotificationSettings.Offers(minutes));
    }

    [Fact]
    public void TheSettingsAreOneRow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NotificationSettings.Rehydrate(2, 3, At));
    }
}
