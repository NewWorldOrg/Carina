using System.Net.WebSockets;

namespace Carina.Api.Tests.Unit;

public sealed class ScriptedWebSocketSelfCheckTests
{
    [Fact]
    public async Task AWireThatIsTakingNothingDoesNotTakeACloseFrameEither()
    {
        var socket = new ScriptedWebSocket { HoldEverySend = TimeSpan.FromSeconds(30) };
        using var patience = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "unread", patience.Token));

        Assert.Null(socket.Closed);
    }

    [Fact]
    public async Task AWireThatWasCutOffRefusesACloseFrameTheWayARealOneDoes()
    {
        var socket = new ScriptedWebSocket();

        socket.Abort();

        WebSocketException refused = await Assert.ThrowsAsync<WebSocketException>(
            () => socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "over", CancellationToken.None));

        Assert.Equal(WebSocketError.InvalidState, refused.WebSocketErrorCode);
        Assert.Null(socket.Closed);
    }

    [Fact]
    public async Task AWireThatWasCutOffRefusesAnythingMoreToSend()
    {
        var socket = new ScriptedWebSocket();

        socket.Abort();

        WebSocketException refused = await Assert.ThrowsAsync<WebSocketException>(
            () => socket.SendAsync(new byte[1], WebSocketMessageType.Binary, true, CancellationToken.None));

        Assert.Equal(WebSocketError.InvalidState, refused.WebSocketErrorCode);
        Assert.Empty(socket.Sent);
    }

    [Fact]
    public async Task AWireThatHasSentItsCloseFrameRefusesASecondOne()
    {
        var socket = new ScriptedWebSocket();

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "over", CancellationToken.None);

        await Assert.ThrowsAsync<WebSocketException>(
            () => socket.CloseOutputAsync(WebSocketCloseStatus.InternalServerError, "again", CancellationToken.None));

        Assert.Equal(WebSocketCloseStatus.NormalClosure, socket.Closed);
    }

    [Fact]
    public async Task AWireStillOpenTakesWhatItIsGiven()
    {
        var socket = new ScriptedWebSocket();

        await socket.SendAsync(new byte[1], WebSocketMessageType.Binary, true, CancellationToken.None);
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "over", CancellationToken.None);

        Assert.Single(socket.Sent);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, socket.Closed);
    }
}
