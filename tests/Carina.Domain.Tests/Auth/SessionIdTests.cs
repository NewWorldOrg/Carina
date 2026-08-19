using Carina.Domain.Auth;

namespace Carina.Domain.Tests.Auth;

public sealed class SessionIdTests
{
    [Fact]
    public void AnIssuedIdIsTheLengthTheColumnExpects()
    {
        SessionId id = SessionId.Issue();

        Assert.Equal(SessionId.Length, id.Value.Length);
    }

    [Fact]
    public void AnIssuedIdSurvivesBeingReadBackAsItself()
    {
        SessionId id = SessionId.Issue();

        Assert.Equal(id, new SessionId(id.Value));
    }

    [Fact]
    public void TwoIssuedIdsAreNotTheSameSession()
    {
        Assert.NotEqual(SessionId.Issue(), SessionId.Issue());
    }

    [Fact]
    public void AnIssuedIdCarriesTheEntropyTheCookieIsSupposedToCarry()
    {
        HashSet<string> issued = [];

        for (int index = 0; index < 200; index++)
        {
            Assert.True(issued.Add(SessionId.Issue().Value));
        }
    }

    [Fact]
    public void AnIssuedIdSurvivesAUrlAndACookieUnescaped()
    {
        SessionId id = SessionId.Issue();

        Assert.DoesNotContain(id.Value, character => character is '+' or '/' or '=');
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-short")]
    public void AnIdThatIsNotTheIssuedShapeIsNotASessionId(string value)
    {
        Assert.Throws<ArgumentException>(() => new SessionId(value));
    }

    [Fact]
    public void AnIdIsNeverNull()
    {
        Assert.Throws<ArgumentNullException>(() => new SessionId(null!));
    }

    [Fact]
    public void AnIdCarryingCharactersOutsideTheAlphabetIsRefused()
    {
        string forged = new('*', SessionId.Length);

        Assert.Throws<ArgumentException>(() => new SessionId(forged));
    }
}
