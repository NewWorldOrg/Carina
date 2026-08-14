using System.Text.RegularExpressions;

namespace Carina.Contracts.Tests;

public sealed class DriverEventsTests
{
    private static readonly string[] Expected =
    [
        "tuners",
        "sessions",
        "draining",
        "diagnostics",
        "sessionTuned",
        "sessionLockLost",
        "tunerHealthChanged",
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

    [Fact]
    public void TheNamesTheFoundationAgreedComeFirstAndKeepTheirPlace()
    {
        Assert.Equal(
            new[] { "tuners", "sessions", "draining", "diagnostics" },
            DriverEvents.All.Take(4)
        );
    }

    [Theory]
    [InlineData("tuners")]
    [InlineData("draining")]
    [InlineData("sessionTuned")]
    [InlineData("sessionLockLost")]
    [InlineData("tunerHealthChanged")]
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
