namespace Carina.Contracts.Tests;

public sealed class AppEventNameTests
{
    [Fact]
    public void TheTypedNamesAreExactlyTheDeclaredSet()
    {
        Assert.Equal(AppEvents.All, AppEventName.All.Select(name => name.Value));
    }

    [Fact]
    public void EveryTypedNameIsKnown()
    {
        Assert.All(AppEventName.All, name => Assert.True(AppEvents.IsKnown(name.Value)));
    }

    [Fact]
    public void NoTwoNamesShareAnInstance()
    {
        Assert.Equal(AppEventName.All.Count, AppEventName.All.Distinct().Count());
    }

    [Fact]
    public void ANameCarriesNothingButItself()
    {
        Assert.Equal("recordings", AppEventName.Recordings.Value);
        Assert.Equal("recordings", AppEventName.Recordings.ToString());
    }
}
