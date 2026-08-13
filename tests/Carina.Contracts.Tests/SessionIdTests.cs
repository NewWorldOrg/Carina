namespace Carina.Contracts.Tests;

public sealed class SessionIdTests
{
    [Theory]
    [InlineData("abc")]
    [InlineData("rec-20260810-2315-181")]
    [InlineData("A1")]
    public void OrdinaryIdentifiersAreAccepted(string value)
    {
        Assert.Equal(value, SessionId.Parse(value).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../../../etc/passwd")]
    [InlineData("x?a=b")]
    [InlineData("x#c")]
    [InlineData("a\r\nX-Injected: 1")]
    [InlineData("a b")]
    [InlineData("session/1")]
    [InlineData("%2e%2e")]
    public void HostileIdentifiersAreRejected(string value)
    {
        Assert.False(SessionId.TryParse(value, out _));
        Assert.Throws<FormatException>(() => SessionId.Parse(value));
    }

    [Fact]
    public void NullIsRejected()
    {
        Assert.False(SessionId.TryParse(null, out _));
    }

    [Fact]
    public void OverlongIdentifiersAreRejected()
    {
        Assert.True(SessionId.TryParse(new string('a', SessionId.MaxLength), out _));
        Assert.False(SessionId.TryParse(new string('a', SessionId.MaxLength + 1), out _));
    }

    [Fact]
    public void PathsAreBuiltFromACheckedIdentifier()
    {
        var id = SessionId.Parse("abc");

        Assert.Equal("/sessions/abc", DriverEndpoints.Session(id));
        Assert.Equal("/sessions/abc/stream", DriverEndpoints.SessionStream(id));
    }

    [Fact]
    public void AnIdentifierThisBuildCannotTakeReadsAsUnset()
    {
        Assert.True(default(SessionId).IsUnset);
        Assert.False(SessionId.Parse("abc").IsUnset);
    }

    [Fact]
    public void AnUnsetIdentifierSurvivesARoundTrip()
    {
        var snapshot = new SessionSnapshot(
            default,
            SessionPurpose.Live,
            "a0",
            SessionState.Active,
            new DateTimeOffset(2026, 8, 8, 21, 4, 0, TimeSpan.FromHours(9))
        );

        var restored = DriverJson.Deserialize(
            DriverJson.Serialize(snapshot),
            DriverJson.Context.SessionSnapshot
        );

        Assert.Equal(snapshot, restored);
    }
}
