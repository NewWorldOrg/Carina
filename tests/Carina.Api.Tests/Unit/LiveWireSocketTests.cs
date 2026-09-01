using System.Net.WebSockets;
using System.Threading.Channels;

using Carina.Api.Live;
using Carina.Domain.Streaming;

namespace Carina.Api.Tests.Unit;

public sealed class LiveWireSocketTests
{
    private static readonly LiveWireSettings Impatient = new()
    {
        BetweenPings = TimeSpan.FromMilliseconds(30),
        WritePatience = TimeSpan.FromMilliseconds(200),
    };

    private static readonly byte[] Picture = [0x01, 0x02, 0x03];

    [Fact]
    public async Task AFrameIsSentAsOneBinaryMessageCarryingItsOwnHeader()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        frames.Writer.TryWrite(new LiveFrame(LiveChannel.Picture, LivePts.Of(90_000UL), Picture));
        frames.Writer.Complete();

        LiveDeparture departure = await Carry(socket, frames);

        Assert.Equal(LiveDeparture.SourceEnded, departure);
        Assert.Equal(
            [0x01, 0, 0, 0, 0, 0, 0x01, 0x5f, 0x90, 0x01, 0x02, 0x03],
            socket.Sent[0]);
    }

    [Fact]
    public async Task TheHeaderGoesOutBeforeTheFirstPictureThatNeedsIt()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        frames.Writer.TryWrite(new LiveFrame(LiveChannel.PictureHeader, LivePts.Start, Picture));
        frames.Writer.TryWrite(new LiveFrame(LiveChannel.Picture, LivePts.Of(1UL), Picture));
        frames.Writer.Complete();

        await Carry(socket, frames);

        Assert.Equal([0x00, 0x01], socket.Sent.Select(message => message[0]).ToArray());
    }

    [Fact]
    public async Task TheSourceRunningOutIsANormalEnding()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        frames.Writer.Complete();

        Assert.Equal(LiveDeparture.SourceEnded, await Carry(socket, frames));
        Assert.Equal(WebSocketCloseStatus.NormalClosure, socket.Closed);
    }

    [Fact]
    public async Task TheSourceBreakingIsNotANormalEnding()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        frames.Writer.Complete(new InvalidOperationException("the transcoder stopped"));

        Assert.Equal(LiveDeparture.SourceBroke, await Carry(socket, frames));
        Assert.Equal(WebSocketCloseStatus.InternalServerError, socket.Closed);
    }

    [Fact]
    public void EveryEndingSaysWhyInWordsTheViewerIsGiven()
    {
        Assert.All(
            Enum.GetValues<LiveDeparture>(),
            departure => Assert.NotEmpty(LiveDepartures.Because(departure)));
    }

    [Fact]
    public async Task APingGoesOutWhileTheSourceHasNothingToSay()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();
        using var stop = new CancellationTokenSource();

        Task<LiveDeparture> carrying = Carry(socket, frames, stop.Token);

        await Until(() => socket.Sent.Count >= 2);
        await stop.CancelAsync();
        await carrying;

        Assert.All(
            socket.Sent,
            message => Assert.Equal(LiveControls.Frame(LiveControl.Ping).ToArray(), message));
    }

    [Fact]
    public async Task NoPingGoesOutAfterTheWireHasBeenToldToStop()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();
        using var stop = new CancellationTokenSource();

        await stop.CancelAsync();

        Assert.Equal(LiveDeparture.ViewerLeft, await Carry(socket, frames, stop.Token));
        Assert.Empty(socket.Sent);
    }

    [Fact]
    public async Task NoPingInterruptsAWireThatIsCarryingPictures()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        for (int written = 0; written < 20; written++)
        {
            frames.Writer.TryWrite(new LiveFrame(LiveChannel.Picture, LivePts.Of((ulong)written), Picture));
        }

        frames.Writer.Complete();

        await Carry(socket, frames);

        Assert.Equal(20, socket.Sent.Count);
        Assert.DoesNotContain((byte)LiveChannel.Control, socket.Sent.Select(message => message[0]));
    }

    [Fact]
    public async Task AViewerSayingItIsLeavingIsAnsweredByLettingItGo()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        socket.Say(new WebSocketSaying(
            WebSocketMessageType.Binary,
            LiveControls.Frame(LiveControl.Leaving).ToArray()));

        Assert.Equal(LiveDeparture.ViewerLeft, await Carry(socket, frames));
        Assert.Equal(WebSocketCloseStatus.NormalClosure, socket.Closed);
    }

    [Fact]
    public async Task AViewerClosingTheSocketIsTheSameAsSayingItIsLeaving()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        socket.Say(new WebSocketSaying(WebSocketMessageType.Close, []));

        Assert.Equal(LiveDeparture.ViewerLeft, await Carry(socket, frames));
    }

    [Fact]
    public async Task AViewerAnsweringAPingIsNotAnEnding()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        socket.Say(new WebSocketSaying(
            WebSocketMessageType.Binary,
            LiveControls.Frame(LiveControl.Pong).ToArray()));
        socket.Say(new WebSocketSaying(
            WebSocketMessageType.Binary,
            LiveControls.Frame(LiveControl.Leaving).ToArray()));

        Assert.Equal(LiveDeparture.ViewerLeft, await Carry(socket, frames));
    }

    [Fact]
    public async Task AViewerSayingSomethingThatIsNotAControlMessageIsShownOut()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        socket.Say(new WebSocketSaying(WebSocketMessageType.Binary, [0x40, 0, 0, 0, 0, 0, 0, 0, 0, 0x7f]));

        Assert.Equal(LiveDeparture.SaidSomethingUnknown, await Carry(socket, frames));
        Assert.Equal(WebSocketCloseStatus.InvalidPayloadData, socket.Closed);
    }

    [Fact]
    public async Task AViewerRepeatingWhatOnlyTheServerSaysIsShownOut()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        socket.Say(new WebSocketSaying(
            WebSocketMessageType.Binary,
            LiveControls.Frame(LiveControl.Ping).ToArray()));

        Assert.Equal(LiveDeparture.SaidSomethingUnknown, await Carry(socket, frames));
    }

    [Fact]
    public async Task AViewerPushingBytesUpAPictureChannelIsShownOut()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        socket.Say(new WebSocketSaying(
            WebSocketMessageType.Binary,
            new LiveFrame(LiveChannel.Picture, LivePts.Start, Picture).ToArray()));

        Assert.Equal(LiveDeparture.SaidSomethingUnknown, await Carry(socket, frames));
    }

    [Fact]
    public async Task AViewerTypingWordsAtTheWireIsShownOut()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        socket.Say(new WebSocketSaying(WebSocketMessageType.Text, "seek 42"u8.ToArray()));

        Assert.Equal(LiveDeparture.SaidSomethingUnknown, await Carry(socket, frames));
    }

    [Fact]
    public async Task AViewerSendingMoreThanTheWireTakesIsShownOut()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        socket.Say(new WebSocketSaying(
            WebSocketMessageType.Binary,
            new byte[Impatient.LargestFrameFromAViewer + 64]));

        Assert.Equal(LiveDeparture.SaidMoreThanTheWireTakes, await Carry(socket, frames));
        Assert.Equal(WebSocketCloseStatus.MessageTooBig, socket.Closed);
    }

    [Fact]
    public async Task AViewerThatStopsReadingIsGivenUpOnRatherThanHoldingTheSource()
    {
        var socket = new ScriptedWebSocket { HoldEverySend = TimeSpan.FromSeconds(30) };
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        frames.Writer.TryWrite(new LiveFrame(LiveChannel.Picture, LivePts.Start, Picture));

        Assert.Equal(LiveDeparture.ViewerStoppedReading, await Carry(socket, frames));
        Assert.True(socket.Aborted);
    }

    [Fact]
    public async Task AWireCarryingAFrameLargerThanAnythingTheFragmenterHoldsStillSendsItWhole()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        frames.Writer.TryWrite(new LiveFrame(LiveChannel.Picture, LivePts.Start, new byte[4 * 1024 * 1024]));
        frames.Writer.Complete();

        await Carry(socket, frames);

        Assert.Equal(LiveFrame.HeaderLength + (4 * 1024 * 1024), socket.Sent[0].Length);
    }

    [Fact]
    public async Task TheAppShuttingDownIsNotTheViewerLeaving()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();
        using var stopping = new CancellationTokenSource();
        using var stop = new CancellationTokenSource();

        Task<LiveDeparture> carrying = new LiveWireSocket(socket, Impatient)
            .CarryAsync(frames.Reader, stopping.Token, stop.Token);

        await stopping.CancelAsync();
        await stop.CancelAsync();

        Assert.Equal(LiveDeparture.ServerStopping, await carrying);
        Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, socket.Closed);
    }

    private static Task<LiveDeparture> Carry(
        ScriptedWebSocket socket,
        Channel<LiveFrame> frames,
        CancellationToken cancellationToken = default)
        => new LiveWireSocket(socket, Impatient)
            .CarryAsync(frames.Reader, CancellationToken.None, cancellationToken);

    private static async Task Until(Func<bool> settled)
    {
        for (int tries = 0; tries < 200; tries++)
        {
            if (settled())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.Fail("The wire never carried what was expected of it.");
    }
}
