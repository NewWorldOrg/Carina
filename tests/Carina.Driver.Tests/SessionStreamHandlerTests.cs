using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Ipc;
using Carina.Driver.Sessions;
using Carina.Driver.Transport;
using Carina.Driver.Tuning;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Driver.Tests;

public sealed class SessionStreamHandlerTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private readonly string root = Directory.CreateTempSubdirectory("carina-stream-").FullName;
    private readonly ManualTimeProvider clock = new(new DateTimeOffset(2026, 8, 13, 21, 0, 0, TimeSpan.Zero));
    private readonly List<TunerSession> started = [];

    public void Dispose()
    {
        foreach (TunerSession session in started)
        {
            session.Dispose();
        }

        Directory.Delete(root, recursive: true);
    }

    private sealed class RecordedLifetime : IHttpRequestLifetimeFeature
    {
        private readonly CancellationTokenSource aborting = new();

        public CancellationToken RequestAborted
        {
            get => aborting.Token;
            set { }
        }

        public bool Aborted { get; private set; }

        public void Abort()
        {
            Aborted = true;
            aborting.Cancel();
        }
    }

    private sealed class RecordedBody : MemoryStream
    {
        private readonly List<int> writes = [];

        public int[] Writes
        {
            get
            {
                lock (writes)
                {
                    return [.. writes];
                }
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Record(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Record(buffer.Length);
            base.Write(buffer);
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken
        )
        {
            Record(count);

            return base.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            Record(buffer.Length);

            return base.WriteAsync(buffer, cancellationToken);
        }

        private void Record(int count)
        {
            if (count is 0)
            {
                return;
            }

            lock (writes)
            {
                writes.Add(count);
            }
        }
    }

    private sealed class TakesEveryByteAndSendsNone : MemoryStream
    {
        private readonly SemaphoreSlim goodbye = new(0);

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            if (Length is 0)
            {
                await base.FlushAsync(cancellationToken);

                return;
            }

            goodbye.Release();

            var neverSent = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            using CancellationTokenRegistration letGo = cancellationToken.Register(
                () => neverSent.TrySetCanceled(cancellationToken)
            );

            await neverSent.Task;
        }

        public bool ReachedTheGoodbyeFlush(TimeSpan within) => goodbye.Wait(within);
    }

    private static (HttpContext Context, RecordedLifetime Lifetime, RecordedBody Body) Ask(
        string sessionId,
        string? subscriber = null
    )
    {
        var body = new RecordedBody();
        (HttpContext context, RecordedLifetime lifetime) = AskThrough(body, sessionId, subscriber);

        return (context, lifetime, body);
    }

    private static (HttpContext Context, RecordedLifetime Lifetime) AskThrough(
        Stream body,
        string sessionId,
        string? subscriber = null
    )
    {
        var lifetime = new RecordedLifetime();

        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(
            new HttpRequestFeature
            {
                Method = "GET",
                QueryString = subscriber is null
                    ? string.Empty
                    : $"?{DriverEndpoints.SubscriberQuery}={subscriber}",
            }
        );
        features.Set<IHttpResponseFeature>(new HttpResponseFeature());
        features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(body));
        features.Set<IHttpRequestLifetimeFeature>(lifetime);

        var context = new DefaultHttpContext(features);
        context.Request.RouteValues["id"] = sessionId;

        return (context, lifetime);
    }

    private static async Task<bool> Settles(Task work, TimeSpan within)
    {
        try
        {
            await work.WaitAsync(within);

            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    [Fact]
    public async Task AViewerThatStoppedReadingDoesNotHoldTheDriverOpen()
    {
        TunerSessionManager manager = Manager();
        TunerSession session = Begin(manager, "wedged-one");
        var body = new TakesEveryByteAndSendsNone();
        (HttpContext context, RecordedLifetime lifetime) = AskThrough(body, "wedged-one");

        using var detaching = new CancellationTokenSource();

        Task streaming = SessionStreamHandler.Invoke(
            context,
            manager,
            streamsDetaching: detaching.Token
        );

        await WaitForBytes(body);

        session.Stop();

        Assert.True(
            body.ReachedTheGoodbyeFlush(Patience),
            "The stream never reached the flush it says goodbye with, so this test never got near the socket it is about."
        );

        Assert.False(
            streaming.IsCompleted,
            "The stream let go of a socket that had taken every byte and sent none, before anything had told it to."
        );

        detaching.Cancel();

        Assert.True(
            await Settles(streaming, Patience),
            "The driver said its streams are detaching and this one stayed in its goodbye flush, so shutdown waits for a viewer that stopped reading."
        );

        Assert.True(lifetime.Aborted);
    }

    private DriverConfiguration Configuration =>
        new(
            "/run/carina/driver.sock",
            [new OutputRootSettings("primary", root)],
            6,
            new TunerSettings(TunerBackend.Fake),
            [new DeviceSettings("adapter0", DeviceKind.Terrestrial)]
        );

    private TunerSessionManager Manager()
    {
        DriverConfiguration configuration = Configuration;

        return new TunerSessionManager(
            configuration,
            new TunerDeviceFactory(configuration, TimeProvider.System),
            clock,
            NullLogger<TunerSessionManager>.Instance
        );
    }

    private TunerSession Begin(TunerSessionManager manager, string sessionId)
    {
        SessionStart start = manager.Begin(
            new StartSessionRequest
            {
                SessionId = SessionId.Parse(sessionId),
                Purpose = SessionPurpose.Live,
                Tuning = new TuningRequest(TunerKind.Terrestrial, 55),
            }
        );

        Assert.True(start.TryGetSession(out TunerSession? session), start.Detail);
        started.Add(session);

        return session;
    }

    private static async Task WaitForBytes(MemoryStream body)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + Patience;

        while (body.Length is 0)
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                Assert.Fail("The stream never carried a byte.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }
    }

    [Fact]
    public async Task EveryWriteToTheResponseBodyIsAWholeNumberOfPacketsSoAConsumerNeverParsesAcrossAWrite()
    {
        TunerSessionManager manager = Manager();
        TunerSession session = Begin(manager, "aligned-one");
        (HttpContext? context, RecordedLifetime _, RecordedBody? body) = Ask("aligned-one");

        Task streaming = SessionStreamHandler.Invoke(context, manager);

        await WaitForBytes(body);

        session.Stop();

        await streaming.WaitAsync(Patience);

        int[] writes = body.Writes;

        Assert.NotEmpty(writes);
        Assert.All(writes, write => Assert.Equal(0, write % TsPacketReader.PacketLength));
    }

    [Fact]
    public void TheSessionAsksTheDeviceForAWholeNumberOfPacketsSoNoChunkItPublishesCanSplitOne()
    {
        Assert.Equal(0, TunerSession.DefaultChunkSize % TsPacketReader.PacketLength);
    }

    [Fact]
    public async Task AStreamThatWasStoppedCleanlyIsNotAborted()
    {
        TunerSessionManager manager = Manager();
        TunerSession session = Begin(manager, "clean-one");
        (HttpContext? context, RecordedLifetime? lifetime, RecordedBody? body) = Ask("clean-one");

        Task streaming = SessionStreamHandler.Invoke(context, manager);

        await WaitForBytes(body);

        session.Stop();

        await streaming.WaitAsync(Patience);

        Assert.False(lifetime.Aborted);
        Assert.True(body.Length > 0);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task AStreamThatFailedMidwayIsAborted()
    {
        TunerSessionManager manager = Manager();
        TunerSession session = Begin(manager, "broken-one");
        (HttpContext? context, RecordedLifetime? lifetime, RecordedBody? body) = Ask("broken-one");

        Task streaming = SessionStreamHandler.Invoke(context, manager);

        await WaitForBytes(body);

        session.Broadcaster.Close(new IOException("the tuning was lost"));

        await streaming.WaitAsync(Patience);

        Assert.True(lifetime.Aborted);
        Assert.True(body.Length > 0);
    }

    [Fact]
    public async Task AReaderArrivingWhileTheSessionConcludesIsRefusedNotHandedAnEmptySuccess()
    {
        TunerSessionManager manager = Manager();
        TunerSession session = Begin(manager, "closing-one");

        session.Broadcaster.Close(null);

        (HttpContext? context, RecordedLifetime? lifetime, RecordedBody? body) = Ask("closing-one");

        await SessionStreamHandler.Invoke(context, manager);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.False(lifetime.Aborted);

        session.Stop();
        session.WaitForEnd(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task AStreamWhoseEndTheDriverCannotVouchForIsAborted()
    {
        TunerSessionManager manager = Manager();
        TunerSession session = Begin(manager, "unvouched-one");
        (HttpContext? context, RecordedLifetime? lifetime, RecordedBody? body) = Ask("unvouched-one");

        Task streaming = SessionStreamHandler.Invoke(
            context,
            manager,
            TimeSpan.FromMilliseconds(200)
        );

        await WaitForBytes(body);

        session.Broadcaster.Close(null);

        await streaming.WaitAsync(Patience);

        Assert.True(lifetime.Aborted);

        session.Stop();
        session.WaitForEnd(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task AStreamCutShortByTheDrainCapIsAbortedAndNotClosedCleanly()
    {
        TunerSessionManager manager = Manager();
        TunerSession session = Begin(manager, "drained-one");
        (HttpContext? context, RecordedLifetime? lifetime, RecordedBody? body) = Ask("drained-one");

        Task streaming = SessionStreamHandler.Invoke(context, manager);

        await WaitForBytes(body);

        session.Broadcaster.Close(
            new OperationCanceledException(
                "The shutdown grace period ran out while 'drained-one' was still recording."
            )
        );

        await streaming.WaitAsync(Patience);

        Assert.True(lifetime.Aborted);
    }

    [Fact]
    public async Task ASurveyReaderThatWasCutShortIsAborted()
    {
        TunerSessionManager manager = Manager();
        TunerSession session = Begin(manager, "cut-one");
        (HttpContext? context, RecordedLifetime? lifetime, RecordedBody? body) = Ask("cut-one", DriverEndpoints.SurveySubscriber);

        Task streaming = SessionStreamHandler.Invoke(context, manager);

        await WaitForBytes(body);

        session.Broadcaster.Close(
            new TimeoutException("the reader never took the stream")
        );

        await streaming.WaitAsync(Patience);

        Assert.True(lifetime.Aborted);
    }

    [Fact]
    public async Task AReaderThatWentAwayLeavesNoSubscription()
    {
        TunerSessionManager manager = Manager();
        TunerSession session = Begin(manager, "gone-one");
        (HttpContext? context, RecordedLifetime? lifetime, RecordedBody? body) = Ask("gone-one");

        Task streaming = SessionStreamHandler.Invoke(context, manager);

        await WaitForBytes(body);

        Assert.Equal(1, session.Broadcaster.SubscriberCount);

        lifetime.Abort();

        await streaming.WaitAsync(Patience);

        Assert.Equal(0, session.Broadcaster.SubscriberCount);
    }

    [Fact]
    public async Task ASessionIdTheDriverCannotReadIsRefused()
    {
        TunerSessionManager manager = Manager();
        (HttpContext? context, RecordedLifetime? lifetime, RecordedBody _) = Ask("not_valid");

        await SessionStreamHandler.Invoke(context, manager);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.False(lifetime.Aborted);
    }

    [Fact]
    public async Task ARiderThatCameAlongForTheGuideIsServedAndDoesNotHoldTheSessionUp()
    {
        TunerSessionManager manager = Manager();
        TunerSession session = Begin(manager, "ridden-one");
        (HttpContext? context, RecordedLifetime? lifetime, RecordedBody? body) = Ask(
            "ridden-one",
            DriverEndpoints.PiggybackSubscriber);

        Task streaming = SessionStreamHandler.Invoke(context, manager);

        await WaitForBytes(body);

        Assert.Equal(1, session.Broadcaster.SubscriberCount);

        lifetime.Abort();

        await streaming.WaitAsync(Patience);

        Assert.Equal(0, session.Broadcaster.SubscriberCount);
    }

    [Fact]
    public async Task ASubscriberKindTheDriverDoesNotKnowIsRefused()
    {
        TunerSessionManager manager = Manager();
        Begin(manager, "kinded-one");
        (HttpContext? context, RecordedLifetime? lifetime, RecordedBody _) = Ask("kinded-one", "cameraman");

        await SessionStreamHandler.Invoke(context, manager);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.False(lifetime.Aborted);
    }

    [Fact]
    public async Task ASessionTheDriverDoesNotHoldIsNotFound()
    {
        TunerSessionManager manager = Manager();
        (HttpContext? context, RecordedLifetime _, RecordedBody _) = Ask("absent-one");

        await SessionStreamHandler.Invoke(context, manager);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task ReadersBeyondTheLimitAreTurnedAway()
    {
        TunerSessionManager manager = Manager();
        TunerSession session = Begin(manager, "crowded-one");

        var held = new List<SessionSubscription>();
        for (int taken = 0; taken < session.Broadcaster.SubscriberLimit; taken++)
        {
            Assert.True(session.Broadcaster.TrySubscribe(SubscriberKind.Viewer, out SessionSubscription? one));
            held.Add(one);
        }

        (HttpContext? context, RecordedLifetime _, RecordedBody _) = Ask("crowded-one");

        await SessionStreamHandler.Invoke(context, manager);

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);

        foreach (SessionSubscription subscription in held)
        {
            session.Broadcaster.Unsubscribe(subscription);
        }
    }
}
