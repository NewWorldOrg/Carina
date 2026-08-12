namespace Carina.Contracts.Tests;

/// <summary>
/// The identifier ends up in a request path on the privileged process, so the shape
/// is checked where the value enters. These are the inputs that would otherwise
/// change which endpoint a request reaches.
/// </summary>
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

    // A driver that minted an identifier this build would not have minted is still
    // reporting something; refusing to read it would lose the whole answer. It is
    // rejected instead of carried, because carrying it means putting it in a path.
    [Fact]
    public void AnIdentifierOutsideTheShapeIsRejectedOnRead()
    {
        Assert.Throws<System.Text.Json.JsonException>(
            () =>
                DriverJson.Deserialize(
                    """{"sessionId":"../x","purpose":"live","deviceId":"a0","state":"active","startedAt":"2026-08-08T21:04:00+09:00","endsAt":null}""",
                    DriverJson.Context.SessionSnapshot
                )
        );
    }
}
