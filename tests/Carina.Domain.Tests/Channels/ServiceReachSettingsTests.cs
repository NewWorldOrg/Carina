using Carina.Domain.Channels;

namespace Carina.Domain.Tests.Channels;

public sealed class ServiceReachSettingsTests
{
    private static readonly DateTime Now = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AMachineNobodyHasConfiguredWaitsADayBeforeCallingASystemMissing()
    {
        ServiceReachSettings settings = ServiceReachSettings.Default(Now);

        Assert.Equal(24, settings.HoursOfSilence);
        Assert.Equal(TimeSpan.FromHours(24), settings.Silence);
        Assert.Equal(ServiceReachSettings.TheOnlyRow, settings.Id);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(24)]
    [InlineData(720)]
    public void AWaitInsideTheRangeIsKept(int hours)
    {
        ServiceReachSettings settings = ServiceReachSettings.Default(Now);

        settings.AllowSilenceFor(hours, Now);

        Assert.Equal(hours, settings.HoursOfSilence);
        Assert.Equal(TimeSpan.FromHours(hours), settings.Silence);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(721)]
    [InlineData(-1)]
    public void AWaitOutsideTheRangeIsRefusedOnTheWayIn(int hours)
    {
        ArgumentOutOfRangeException thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => ServiceReachSettings.Rehydrate(ServiceReachSettings.TheOnlyRow, hours, Now));

        Assert.Equal("hoursOfSilence", thrown.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(721)]
    public void AWaitOutsideTheRangeIsRefusedOnTheWayThrough(int hours)
    {
        ServiceReachSettings settings = ServiceReachSettings.Default(Now);

        ArgumentOutOfRangeException thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => settings.AllowSilenceFor(hours, Now));

        Assert.Equal("hoursOfSilence", thrown.ParamName);
        Assert.Equal(24, settings.HoursOfSilence);
    }

    [Fact]
    public void ChangingTheWaitRecordsWhenItChanged()
    {
        ServiceReachSettings settings = ServiceReachSettings.Default(Now);

        settings.AllowSilenceFor(48, Now.AddHours(3));

        Assert.Equal(Now.AddHours(3), settings.UpdatedAt);
    }

    [Fact]
    public void TheRangeIsOneHourToThirtyDays()
    {
        Assert.Equal(1, ServiceReachSettings.ShortestHoursOfSilence);
        Assert.Equal(720, ServiceReachSettings.LongestHoursOfSilence);
    }
}
