using System.Threading.Channels;

using Carina.Api.Live;
using Carina.Domain.Streaming;

namespace Carina.Api.Tests.Unit;

public sealed class LiveWireSocketProgressTests
{
    private static readonly LiveWireSettings Impatient = new()
    {
        BetweenPings = TimeSpan.FromMilliseconds(30),
        WritePatience = TimeSpan.FromMilliseconds(200),
    };

    private static readonly byte[] Picture = [0x01, 0x02, 0x03];

    private static readonly LiveStartup Starting = LiveStartup.NotStarted
        .Reaching(LiveStartupSegment.TranscoderStarted, TimeSpan.FromSeconds(8))
        .Reaching(LiveStartupSegment.InitReached, TimeSpan.FromMilliseconds(8100));

    [Fact]
    public async Task ProgressGoesOutRightAfterTheHandshakeWhileTheStartupIsStillRunning()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();
        using var stop = new CancellationTokenSource();

        Task<LiveDeparture> carrying = Carry(socket, frames, new HeldStartup(Starting), stop.Token);

        await Until(() => socket.Sent.Count >= 1);
        await stop.CancelAsync();
        await carrying;

        LiveFrame first = LiveFrame.Read(socket.Sent[0]).Frame!;

        Assert.Equal(LiveChannel.Control, first.Channel);

        LiveStartupReading read = LiveStartup.ReadProgress(first.Payload.Span);

        Assert.Null(read.Fault);
        Assert.Equal(TimeSpan.FromSeconds(8), read.Startup!.At(LiveStartupSegment.TranscoderStarted));
        Assert.Equal(TimeSpan.FromMilliseconds(8100), read.Startup.At(LiveStartupSegment.InitReached));
        Assert.False(read.Startup.Reached(LiveStartupSegment.FirstPicture));
    }

    [Fact]
    public async Task AProgressReportRidesTheControlChannelButIsNotSomethingAViewerCouldEverSay()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();
        using var stop = new CancellationTokenSource();

        Task<LiveDeparture> carrying = Carry(socket, frames, new HeldStartup(Starting), stop.Token);

        await Until(() => socket.Sent.Count >= 1);
        await stop.CancelAsync();
        await carrying;

        LiveFrame first = LiveFrame.Read(socket.Sent[0]).Frame!;

        Assert.Equal(LiveChannel.Control, first.Channel);
        Assert.NotEqual(1, first.Payload.Length);
        Assert.Null(LiveControls.SaidByAViewer(first.Payload.Span));
    }

    [Fact]
    public async Task OnceTheFirstPictureIsReachedTheWireFallsBackToTheKeepAlivePing()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();
        using var stop = new CancellationTokenSource();

        LiveStartup done = Starting.Reaching(LiveStartupSegment.FirstPicture, TimeSpan.FromMilliseconds(10100));

        Task<LiveDeparture> carrying = Carry(socket, frames, new HeldStartup(done), stop.Token);

        await Until(() => socket.Sent.Count >= 2);
        await stop.CancelAsync();
        await carrying;

        Assert.All(
            socket.Sent,
            message => Assert.Equal(LiveControls.Frame(LiveControl.Ping).ToArray(), message));
    }

    [Fact]
    public async Task AWireWithNothingToReportSaysNothingButTheKeepAlivePing()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();
        using var stop = new CancellationTokenSource();

        Task<LiveDeparture> carrying = Carry(socket, frames, new HeldStartup(null), stop.Token);

        await Until(() => socket.Sent.Count >= 2);
        await stop.CancelAsync();
        await carrying;

        Assert.All(
            socket.Sent,
            message => Assert.Equal(LiveControls.Frame(LiveControl.Ping).ToArray(), message));
    }

    [Fact]
    public async Task ProgressDoesNotInterruptTheFramesTheSourceIsAlreadySending()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        for (int written = 0; written < 10; written++)
        {
            frames.Writer.TryWrite(new LiveFrame(LiveChannel.Picture, LivePts.Of((ulong)written), Picture));
        }

        frames.Writer.Complete();

        await Carry(socket, frames, new HeldStartup(Starting), CancellationToken.None);

        Assert.Equal(10, socket.Sent.Count(message => message[0] == (byte)LiveChannel.Picture));
    }

    private static Task<LiveDeparture> Carry(
        ScriptedWebSocket socket,
        Channel<LiveFrame> frames,
        ILiveStartup startup,
        CancellationToken cancellationToken)
        => new LiveWireSocket(socket, Impatient, startup)
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

        Assert.Fail("The wire never said what was expected of it.");
    }

    private sealed class HeldStartup(LiveStartup? current) : ILiveStartup
    {
        public LiveStartup? Current { get; } = current;
    }
}
