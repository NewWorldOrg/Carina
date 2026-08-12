using System.Text.RegularExpressions;

namespace Carina.Contracts.Tests;

/// <summary>
/// The set of names is fixed here rather than grown per domain: an event name can
/// never be renamed or removed once shipped, so a name invented in passing would be
/// permanent. A domain may only add a name, and only by editing this expectation.
/// </summary>
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

    // A name that is not in the set must not be treated as an event by the server
    // side either. The browser ignores what it does not know, which is what lets the
    // server ship a new name first; that tolerance is not a licence to invent names.
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
