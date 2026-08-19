using Carina.Contracts;
using Carina.Driver.Ipc;

namespace Carina.Driver.Tests;

public sealed class DriverGreetingTests
{
    [Fact]
    public void EveryPurposeBeyondTheBaselineIsDeclaredSoAnAppCanTellItIsSafeToAskFor()
    {
        foreach (SessionPurpose purpose in Enum.GetValues<SessionPurpose>())
        {
            if (SessionPurposes.Capability(purpose) is { } capability)
            {
                Assert.Contains(capability, DriverGreeting.Capabilities);
            }
        }
    }

    [Fact]
    public void TheHurriedSurveyIsAmongTheDeclaredPurposes()
    {
        Assert.Contains(
            DriverCapabilities.Purpose("surveyNow"),
            DriverGreeting.Capabilities
        );
    }

    [Fact]
    public void ThePurposesEveryDriverHasAlwaysAcceptedAreNotDeclaredAgain()
    {
        Assert.DoesNotContain(
            DriverGreeting.Capabilities,
            capability => DriverCapabilities.PurposeIn(capability)
                is "recording" or "live" or "survey" or "scan");
    }
}
