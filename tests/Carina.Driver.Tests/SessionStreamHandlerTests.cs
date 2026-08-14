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
        foreach (var session in started)
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

    private static (HttpContext Context, RecordedLifetime Lifetime, RecordedBody Body) Ask(
        string sessionId,
        string? subscriber = null
    )
    {
        var body = new RecordedBody();
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

        return (context, lifetime, body);
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
        var configuration = Configuration;

        return new TunerSessionManager(
            configuration,
            new TunerDeviceFactory(configuration, TimeProvider.System),
            clock,
            NullLogger<TunerSessionManager>.Instance
        );
    }

    private TunerSession Begin(TunerSessionManager manager, string sessionId)
    {
        var start = manager.Begin(
            new StartSessionRequest
            {
                SessionId = SessionId.Parse(sessionId),
                Purpose = SessionPurpose.Live,
                Tuning = new TuningRequest(TunerKind.Terrestrial, 27),
            }
        );

        Assert.True(start.TryGetSession(out var session), start.Detail);
        started.Add(session);

        return session;
    }

    private static async Task WaitForBytes(MemoryStream body)
    {
        var deadline = DateTimeOffset.UtcNow + Patience;

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
        var manager = Manager();
        var session = Begin(manager, "aligned-one");
        var (context, _, body) = Ask("aligned-one");

        var streaming = SessionStreamHandler.Invoke(context, manager);

        await WaitForBytes(body);

        session.Stop();

        await streaming.WaitAsync(Patience);

        var writes = body.Writes;

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
        var manager = Manager();
        var session = Begin(manager, "clean-one");
        var (context, lifetime, body) = Ask("clean-one");

        var streaming = SessionStreamHandler.Invoke(context, manager);

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
        var manager = Manager();
        var session = Begin(manager, "broken-one");
        var (context, lifetime, body) = Ask("broken-one");

        var streaming = SessionStreamHandler.Invoke(context, manager);

        await WaitForBytes(body);

        session.Broadcaster.Close(new IOException("the tuning was lost"));

        await streaming.WaitAsync(Patience);

        Assert.True(lifetime.Aborted);
        Assert.True(body.Length > 0);
    }

    [Fact]
    public async Task AReaderArrivingWhileTheSessionConcludesIsRefusedNotHandedAnEmptySuccess()
    {
        var manager = Manager();
        var session = Begin(manager, "closing-one");

        session.Broadcaster.Close(null);

        var (context, lifetime, body) = Ask("closing-one");

        await SessionStreamHandler.Invoke(context, manager);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.False(lifetime.Aborted);

        session.Stop();
        session.WaitForEnd(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task AStreamWhoseEndTheDriverCannotVouchForIsAborted()
    {
        var manager = Manager();
        var session = Begin(manager, "unvouched-one");
        var (context, lifetime, body) = Ask("unvouched-one");

        var streaming = SessionStreamHandler.Invoke(
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
        var manager = Manager();
        var session = Begin(manager, "drained-one");
        var (context, lifetime, body) = Ask("drained-one");

        var streaming = SessionStreamHandler.Invoke(context, manager);

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
        var manager = Manager();
        var session = Begin(manager, "cut-one");
        var (context, lifetime, body) = Ask("cut-one", DriverEndpoints.SurveySubscriber);

        var streaming = SessionStreamHandler.Invoke(context, manager);

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
        var manager = Manager();
        var session = Begin(manager, "gone-one");
        var (context, lifetime, body) = Ask("gone-one");

        var streaming = SessionStreamHandler.Invoke(context, manager);

        await WaitForBytes(body);

        Assert.Equal(1, session.Broadcaster.SubscriberCount);

        lifetime.Abort();

        await streaming.WaitAsync(Patience);

        Assert.Equal(0, session.Broadcaster.SubscriberCount);
    }

    [Fact]
    public async Task ASessionIdTheDriverCannotReadIsRefused()
    {
        var manager = Manager();
        var (context, lifetime, _) = Ask("not_valid");

        await SessionStreamHandler.Invoke(context, manager);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.False(lifetime.Aborted);
    }

    [Fact]
    public async Task ASubscriberKindTheDriverDoesNotKnowIsRefused()
    {
        var manager = Manager();
        Begin(manager, "kinded-one");
        var (context, lifetime, _) = Ask("kinded-one", "cameraman");

        await SessionStreamHandler.Invoke(context, manager);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.False(lifetime.Aborted);
    }

    [Fact]
    public async Task ASessionTheDriverDoesNotHoldIsNotFound()
    {
        var manager = Manager();
        var (context, _, _) = Ask("absent-one");

        await SessionStreamHandler.Invoke(context, manager);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task ReadersBeyondTheLimitAreTurnedAway()
    {
        var manager = Manager();
        var session = Begin(manager, "crowded-one");

        var held = new List<SessionSubscription>();
        for (var taken = 0; taken < session.Broadcaster.SubscriberLimit; taken++)
        {
            Assert.True(session.Broadcaster.TrySubscribe(SubscriberKind.Viewer, out var one));
            held.Add(one);
        }

        var (context, _, _) = Ask("crowded-one");

        await SessionStreamHandler.Invoke(context, manager);

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);

        foreach (var subscription in held)
        {
            session.Broadcaster.Unsubscribe(subscription);
        }
    }
}
