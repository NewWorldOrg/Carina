using System.Text.RegularExpressions;

namespace Carina.Contracts.Tests;

/// <summary>
/// The driver's event names are as permanent as the app's: a driver already in the
/// field keeps sending the names it was built with. Renaming one here would leave
/// the app listening for something nobody sends.
/// </summary>
public sealed class DriverEventsTests
{
    private static readonly string[] Expected =
    [
        "tuners",
        "sessions",
        "draining",
        "diagnostics",
    ];

    [Fact]
    public void TheSetIsExactlyTheAgreedNames()
    {
        Assert.Equal(Expected, DriverEvents.All);
    }

    [Fact]
    public void EveryNameIsCamelCase()
    {
        Assert.All(
            DriverEvents.All,
            name => Assert.Matches(new Regex("^[a-z][A-Za-z]*$"), name)
        );
    }

    [Theory]
    [InlineData("tuners")]
    [InlineData("draining")]
    public void KnownNamesAreRecognised(string name)
    {
        Assert.True(DriverEvents.IsKnown(name));
    }

    [Theory]
    [InlineData("session")]
    [InlineData("Draining")]
    [InlineData("")]
    public void AnythingElseIsUnknown(string name)
    {
        Assert.False(DriverEvents.IsKnown(name));
    }

    [Fact]
    public void NullIsUnknown()
    {
        Assert.False(DriverEvents.IsKnown(null));
    }
}
