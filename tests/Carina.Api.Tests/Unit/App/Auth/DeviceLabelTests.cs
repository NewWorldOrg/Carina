using Carina.Api.Authentication;
using Carina.Domain.Auth;

namespace Carina.Api.Tests.Unit;

public sealed class DeviceLabelTests
{
    [Fact]
    public void ABrowserThatNamesItselfIsRememberedByThatName()
    {
        Assert.Equal("Mozilla/5.0 (iPad)", DeviceLabel.From("Mozilla/5.0 (iPad)"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void ACallerThatNamesNothingStillGetsALabelTheSessionAccepts(string? userAgent)
    {
        Assert.Equal(DeviceLabel.Unnamed, DeviceLabel.From(userAgent));
    }

    [Fact]
    public void AnOverlongNameIsCutToWhatASessionRowHolds()
    {
        string label = DeviceLabel.From(new string('a', AuthSession.LongestDeviceLabel + 40));

        Assert.Equal(AuthSession.LongestDeviceLabel, label.Length);
    }

    [Fact]
    public void ControlCharactersAreTakenOutBecauseASessionRowRefusesThem()
    {
        Assert.Equal("Mozilla 5.0", DeviceLabel.From("Mozilla\u0007 5.0"));
    }

    [Fact]
    public void EveryLabelItMakesIsOneASessionCanBeStartedWith()
    {
        AuthSession session = AuthSession.Start(
            SessionId.Issue(),
            new Subject("carina"),
            "carina",
            AuthMethod.Local,
            DeviceLabel.From(new string('a', 400)),
            DateTime.UtcNow);

        Assert.Equal(AuthSession.LongestDeviceLabel, session.DeviceLabel.Length);
    }
}
