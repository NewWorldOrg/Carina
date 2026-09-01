using System.Net.WebSockets;
using System.Threading.Channels;

namespace Carina.Api.Tests.Unit;

internal sealed record WebSocketSaying(WebSocketMessageType Type, byte[] Bytes, bool EndOfMessage = true);

internal sealed class ScriptedWebSocket : WebSocket
{
    private readonly Channel<WebSocketSaying> incoming = Channel.CreateUnbounded<WebSocketSaying>();

    private readonly Lock gate = new();

    private readonly List<byte[]> sent = [];

    private WebSocketState state = WebSocketState.Open;

    public override WebSocketCloseStatus? CloseStatus { get; }

    public override string? CloseStatusDescription { get; }

    public override string? SubProtocol => null;

    public override WebSocketState State => state;

    public WebSocketCloseStatus? Closed { get; private set; }

    public string? ClosedBecause { get; private set; }

    public bool Aborted { get; private set; }

    public TimeSpan HoldEverySend { get; set; } = TimeSpan.Zero;

    public IReadOnlyList<byte[]> Sent
    {
        get
        {
            lock (gate)
            {
                return [.. sent];
            }
        }
    }

    public void Say(WebSocketSaying saying) => incoming.Writer.TryWrite(saying);

    public override void Abort()
    {
        Aborted = true;
        state = WebSocketState.Aborted;
        incoming.Writer.TryComplete();
    }

    public override Task CloseAsync(
        WebSocketCloseStatus closeStatus,
        string? statusDescription,
        CancellationToken cancellationToken)
        => CloseOutputAsync(closeStatus, statusDescription, cancellationToken);

    public override Task CloseOutputAsync(
        WebSocketCloseStatus closeStatus,
        string? statusDescription,
        CancellationToken cancellationToken)
    {
        Closed = closeStatus;
        ClosedBecause = statusDescription;
        state = WebSocketState.CloseSent;

        return Task.CompletedTask;
    }

    public override void Dispose() => incoming.Writer.TryComplete();

    public override async Task<WebSocketReceiveResult> ReceiveAsync(
        ArraySegment<byte> buffer,
        CancellationToken cancellationToken)
    {
        WebSocketSaying saying = await incoming.Reader.ReadAsync(cancellationToken);

        if (saying.Type is WebSocketMessageType.Close)
        {
            state = WebSocketState.CloseReceived;

            return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);
        }

        int taken = Math.Min(buffer.Count, saying.Bytes.Length);

        saying.Bytes.AsSpan(0, taken).CopyTo(buffer.AsSpan());

        return new WebSocketReceiveResult(
            taken,
            saying.Type,
            saying.EndOfMessage && taken == saying.Bytes.Length);
    }

    public override async Task SendAsync(
        ArraySegment<byte> buffer,
        WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken cancellationToken)
    {
        if (HoldEverySend > TimeSpan.Zero)
        {
            await Task.Delay(HoldEverySend, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            sent.Add([.. buffer.AsSpan()]);
        }
    }
}
