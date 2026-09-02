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

    private static readonly LiveWireSettings Patient = new()
    {
        BetweenPings = TimeSpan.FromSeconds(10),
        WritePatience = TimeSpan.FromMilliseconds(200),
    };

    private static readonly byte[] Picture = [0x01, 0x02, 0x03];

    private static readonly LiveStartupSegment[] AsTheyHappen =
    [
        LiveStartupSegment.TunerSecured,
        LiveStartupSegment.TranscoderStarted,
        LiveStartupSegment.ChannelLocked,
        LiveStartupSegment.InitReached,
        LiveStartupSegment.FirstPicture,
    ];

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
    public async Task AReportGoesOutEachTimeASegmentIsReachedNotOnlyWhenTheWireFallsQuiet()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();
        using var stop = new CancellationTokenSource();
        var startup = new SteppingStartup();

        Task<LiveDeparture> carrying = new LiveWireSocket(socket, Patient, startup)
            .CarryAsync(frames.Reader, CancellationToken.None, stop.Token);

        await Until(() => socket.Sent.Count >= 1);

        int reached = 0;

        foreach (LiveStartupSegment segment in AsTheyHappen)
        {
            reached++;
            startup.Reach(segment, TimeSpan.FromMilliseconds(100 * reached));

            int expected = reached + 1;

            await Until(() => socket.Sent.Count >= expected);
        }

        await Task.Delay(150);
        await stop.CancelAsync();
        await carrying;

        LiveStartup[] reports = [.. socket.Sent.Select(Reported)];

        Assert.Equal(6, reports.Length);
        Assert.Equal([0, 1, 2, 3, 4, 5], reports.Select(report => report.Timeline.Count(mark => mark.Reached)));
        Assert.True(reports[2].Reached(LiveStartupSegment.TranscoderStarted));
        Assert.False(reports[2].Reached(LiveStartupSegment.ChannelLocked));
        Assert.False(reports[^1].InProgress);
    }

    [Fact]
    public async Task AStepTheWireHadAlreadyReportedIsNotReportedAgainUntilTheNextOne()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();
        using var stop = new CancellationTokenSource();
        var startup = new SteppingStartup();

        startup.Reach(LiveStartupSegment.TunerSecured, TimeSpan.FromMilliseconds(100));
        startup.Reach(LiveStartupSegment.TranscoderStarted, TimeSpan.FromMilliseconds(110));

        Task<LiveDeparture> carrying = new LiveWireSocket(socket, Patient, startup)
            .CarryAsync(frames.Reader, CancellationToken.None, stop.Token);

        await Until(() => socket.Sent.Count >= 1);
        await Task.Delay(150);

        Assert.Single(socket.Sent);

        startup.Reach(LiveStartupSegment.ChannelLocked, TimeSpan.FromMilliseconds(700));

        await Until(() => socket.Sent.Count >= 2);
        await Task.Delay(150);
        await stop.CancelAsync();
        await carrying;

        Assert.Equal(2, socket.Sent.Count);
        Assert.Equal(2, Reported(socket.Sent[0]).Timeline.Count(mark => mark.Reached));
        Assert.Equal(3, Reported(socket.Sent[1]).Timeline.Count(mark => mark.Reached));
    }

    [Fact]
    public async Task TheKeepAlivePingTakesOverOnceTheLastStepHasBeenReported()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();
        using var stop = new CancellationTokenSource();
        var startup = new SteppingStartup();

        Task<LiveDeparture> carrying = new LiveWireSocket(socket, Impatient, startup)
            .CarryAsync(frames.Reader, CancellationToken.None, stop.Token);

        await Until(() => socket.Sent.Count >= 1);

        foreach (LiveStartupSegment segment in AsTheyHappen)
        {
            startup.Reach(segment, TimeSpan.FromMilliseconds(100));
        }

        await Until(() => socket.Sent.Any(message => message.SequenceEqual(LiveControls.Frame(LiveControl.Ping).ToArray())));
        await stop.CancelAsync();
        await carrying;

        byte[][] afterTheLast = [.. socket.Sent.SkipWhile(message => !Reported(message, out LiveStartup? report) || report!.InProgress).Skip(1)];

        Assert.NotEmpty(afterTheLast);
        Assert.All(afterTheLast, message => Assert.Equal(LiveControls.Frame(LiveControl.Ping).ToArray(), message));
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

    private static LiveStartup Reported(byte[] message)
    {
        Assert.True(Reported(message, out LiveStartup? report), "the wire said something that is not a progress report.");

        return report!;
    }

    private static bool Reported(byte[] message, out LiveStartup? report)
    {
        report = null;

        if (LiveFrame.Read(message).Frame is not { Channel: LiveChannel.Control } frame
            || frame.Payload.Length != LiveStartup.PayloadLength)
        {
            return false;
        }

        report = LiveStartup.ReadProgress(frame.Payload.Span).Startup;

        return report is not null;
    }

    private sealed class HeldStartup(LiveStartup? current) : ILiveStartup
    {
        public LiveStartup? Current { get; } = current;

        public Task Advanced { get; } = new TaskCompletionSource().Task;
    }

    private sealed class SteppingStartup : ILiveStartup
    {
        private readonly Lock gate = new();

        private TaskCompletionSource advanced = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private LiveStartup current = LiveStartup.NotStarted;

        public LiveStartup? Current
        {
            get
            {
                lock (gate)
                {
                    return current;
                }
            }
        }

        public Task Advanced
        {
            get
            {
                lock (gate)
                {
                    return advanced.Task;
                }
            }
        }

        public void Reach(LiveStartupSegment segment, TimeSpan at)
        {
            TaskCompletionSource told;

            lock (gate)
            {
                current = current.Reaching(segment, at);
                told = advanced;
                advanced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            told.SetResult();
        }
    }
}
