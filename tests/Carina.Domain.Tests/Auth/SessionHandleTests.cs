using Carina.Domain.Auth;

namespace Carina.Domain.Tests.Auth;

public sealed class SessionHandleTests
{
    [Fact]
    public void AHandleIsNotTheIdItStandsFor()
    {
        SessionId id = SessionId.Issue();

        SessionHandle handle = SessionHandle.Of(id);

        Assert.NotEqual(id.Value, handle.Value);
        Assert.DoesNotContain(id.Value, handle.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameSessionIsAlwaysGivenTheSameHandle()
    {
        SessionId id = SessionId.Issue();

        Assert.Equal(SessionHandle.Of(id), SessionHandle.Of(new SessionId(id.Value)));
    }

    [Fact]
    public void TwoSessionsAreGivenTwoHandles()
    {
        Assert.NotEqual(SessionHandle.Of(SessionId.Issue()), SessionHandle.Of(SessionId.Issue()));
    }

    [Fact]
    public void AHandleSurvivesAUrlUnescaped()
    {
        SessionHandle handle = SessionHandle.Of(SessionId.Issue());

        Assert.Equal(SessionHandle.Length, handle.Value.Length);
        Assert.DoesNotContain(handle.Value, character => character is '+' or '/' or '=');
    }

    [Fact]
    public void AHandleSurvivesBeingReadBackAsItself()
    {
        SessionHandle handle = SessionHandle.Of(SessionId.Issue());

        Assert.Equal(handle, new SessionHandle(handle.Value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-short")]
    [InlineData("an id with spaces in it that is long enough")]
    public void SomethingThatIsNotTheShapeOfAHandleIsNotAHandle(string value)
    {
        Assert.Throws<ArgumentException>(() => new SessionHandle(value));
    }

    [Fact]
    public void AHandleIsNeverNull()
    {
        Assert.Throws<ArgumentNullException>(() => new SessionHandle(null!));
        Assert.Throws<ArgumentNullException>(() => SessionHandle.Of(null!));
    }
}
