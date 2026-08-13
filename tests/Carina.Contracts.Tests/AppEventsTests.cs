using System.Text.RegularExpressions;

namespace Carina.Contracts.Tests;

public sealed class AppEventsTests
{
    private static readonly string[] Expected =
    [
        "tuners",
        "programs",
        "epgCollection",
        "reservations",
        "rules",
        "recordings",
        "quality",
        "live",
        "encodeJobs",
    ];

    [Fact]
    public void TheSetIsExactlyTheAgreedNames()
    {
        Assert.Equal(Expected, AppEvents.All);
    }

    [Fact]
    public void EveryNameIsCamelCase()
    {
        Assert.All(
            AppEvents.All,
            name => Assert.Matches(new Regex("^[a-z][A-Za-z]*$"), name)
        );
    }

    [Theory]
    [InlineData("recordings")]
    [InlineData("epgCollection")]
    public void KnownNamesAreRecognised(string name)
    {
        Assert.True(AppEvents.IsKnown(name));
    }

    [Theory]
    [InlineData("recording")]
    [InlineData("Recordings")]
    [InlineData("recordingChanged")]
    [InlineData("")]
    public void AnythingElseIsUnknown(string name)
    {
        Assert.False(AppEvents.IsKnown(name));
    }
}
