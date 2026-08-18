using System.Net;
using System.Net.Http.Headers;
using System.Text;

using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Events;
using Carina.Driver.Ipc;
using Carina.Driver.Recording;
using Carina.Driver.Sessions;
using Carina.Driver.Tuning;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Carina.Driver.Tests;

public sealed class DriverApiTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static CancellationToken Soon() =>
        new CancellationTokenSource(Patience).Token;

    [Fact]
    public async Task HealthAnswersWithTheGreeting()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage response = await client.GetAsync(DriverEndpoints.Health, Soon());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        DriverHello? hello = await DriverUnderTest.Read(response, DriverJson.Context.DriverHello);

        Assert.NotNull(hello);
        Assert.Equal(DriverProtocol.Version, hello.ProtocolVersion);
        Assert.True(hello.Supports(DriverCapabilities.Recording));
        Assert.True(hello.Supports(DriverCapabilities.Live));
        Assert.True(hello.Supports(DriverCapabilities.TypedTuning));
    }

    [Fact]
    public async Task ATuneTheOlderParametersCannotNameIsServedFromTheTypedOnes()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        var tune = TuneParams.Bs(15, 50001);
        StartSessionRequest request = DriverUnderTest.Live("typed-tune") with
        {
            Tuning = tune.ToLegacyRequest(),
            Tune = tune,
        };

        using HttpResponseMessage created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(request),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        SessionSnapshot? snapshot = await DriverUnderTest.Read(created, DriverJson.Context.SessionSnapshot);

        Assert.NotNull(snapshot);
        Assert.Equal("fake-satellite", snapshot.DeviceId);
    }

    [Fact]
    public async Task TunersAnswerWithEveryDeclaredDevice()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage response = await client.GetAsync(DriverEndpoints.Tuners, Soon());
        IReadOnlyList<TunerSnapshot>? tuners = await DriverUnderTest.Read(
            response,
            DriverJson.Context.IReadOnlyListTunerSnapshot
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(tuners);
        Assert.Equal(3, tuners.Count);
        Assert.Contains(tuners, tuner => tuner.State is TunerState.Disabled);
    }

    [Fact]
    public async Task ADriverThatHoldsNothingListsNothing()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage response = await client.GetAsync(DriverEndpoints.Sessions, Soon());
        IReadOnlyList<SessionSnapshot>? sessions = await DriverUnderTest.Read(
            response,
            DriverJson.Context.IReadOnlyListSessionSnapshot
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(sessions);
        Assert.Empty(sessions);
    }

    [Fact]
    public async Task TheLocationOfACreatedSessionCanBeFetched()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("located-one")),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using HttpResponseMessage fetched = await client.GetAsync(
            created.Headers.Location?.ToString(),
            Soon()
        );

        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);

        SessionSnapshot? snapshot = await DriverUnderTest.Read(fetched, DriverJson.Context.SessionSnapshot);

        Assert.NotNull(snapshot);
        Assert.Equal("located-one", snapshot.SessionId.Value);

        using HttpResponseMessage missing = await client.GetAsync(
            DriverEndpoints.Session(SessionId.Parse("nobody")),
            Soon()
        );

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task ShutdownDoesNotWaitForAnAttachedViewer()
    {
        DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("watched-one")),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using HttpResponseMessage streaming = await client.GetAsync(
            $"{DriverEndpoints.Session(SessionId.Parse("watched-one"))}/stream",
            HttpCompletionOption.ResponseHeadersRead,
            Soon()
        );

        Assert.Equal(HttpStatusCode.OK, streaming.StatusCode);

        Stream body = await streaming.Content.ReadAsStreamAsync(Soon());
        byte[] buffer = new byte[TsPacketLength];
        Assert.True(await body.ReadAsync(buffer, Soon()) > 0);

        TunerSessionManager manager = driver.Service<TunerSessionManager>();

        Assert.True(manager.TryGet(SessionId.Parse("watched-one"), out TunerSession? session));

        await Until(
            () => session.Broadcaster.SubscriberCount is 1,
            "The viewer never showed up on the session it asked to watch."
        );

        DriverLifecycle lifecycle = driver.Service<DriverLifecycle>();
        Task stopping = driver.BeginStop();

        await Until(
            () => session.Broadcaster.SubscriberCount is 0,
            "The driver never let go of a viewer that had stopped reading, so shutdown is waiting for it."
        );

        await Until(
            () => stopping.IsCompleted,
            "Shutdown never finished, though the viewer it was serving had been let go."
        );

        await stopping;

        Assert.True(
            lifecycle.StreamsDetaching.IsCancellationRequested,
            "Shutdown finished without telling the streams they are detaching."
        );

        Assert.True(
            session.Concluded,
            "Shutdown finished with the watched session still holding its tuner."
        );

        await TheStreamEnds(body, Soon());
        await driver.DisposeAsync();
    }

    [Fact]
    public async Task TheSocketKeepsAnsweringWhileTheDriverDrains()
    {
        DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(
                DriverUnderTest.Recording("lingering", DateTimeOffset.UtcNow.AddMinutes(10))
            ),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        Task stopping = driver.BeginStop();

        DateTimeOffset deadline = DateTimeOffset.UtcNow + Patience;

        while (true)
        {
            using HttpResponseMessage polled = await client.GetAsync(DriverEndpoints.Health, Soon());
            DriverHello? hello = await DriverUnderTest.Read(polled, DriverJson.Context.DriverHello);

            if (hello is { Draining: true })
            {
                break;
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                Assert.Fail("The driver never said it was draining.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        Assert.False(stopping.IsCompleted);

        using HttpResponseMessage listed = await client.GetAsync(DriverEndpoints.Sessions, Soon());
        IReadOnlyList<SessionSnapshot>? sessions = await DriverUnderTest.Read(
            listed,
            DriverJson.Context.IReadOnlyListSessionSnapshot
        );

        Assert.NotNull(sessions);

        SessionSnapshot recording = Assert.Single(sessions);

        Assert.Equal("lingering", recording.SessionId.Value);
        Assert.Equal(SessionState.Active, recording.State);

        using HttpResponseMessage diagnosed = await client.GetAsync(DriverEndpoints.Diagnostics, Soon());

        Assert.Equal(HttpStatusCode.OK, diagnosed.StatusCode);

        using HttpResponseMessage listening = await client.GetAsync(
            DriverEndpoints.Events,
            HttpCompletionOption.ResponseHeadersRead,
            Soon()
        );

        Assert.Equal(HttpStatusCode.OK, listening.StatusCode);

        using HttpResponseMessage refused = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("latecomer")),
            Soon()
        );

        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);

        DriverProblem? problem = await DriverUnderTest.Read(refused, DriverJson.Context.DriverProblem);

        Assert.NotNull(problem);
        Assert.Equal("draining", problem.Title);

        using HttpResponseMessage stopped = await client.DeleteAsync(
            $"{DriverEndpoints.Session(SessionId.Parse("lingering"))}?reason=test",
            Soon()
        );

        Assert.True(
            stopped.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.OK,
            $"Stopping the recording during the drain answered {stopped.StatusCode}."
        );

        await stopping.WaitAsync(TimeSpan.FromSeconds(20));

        await driver.DisposeAsync();
    }

    [Fact]
    public async Task AnEventsListenerHearsDrainingWhenTheDriverShutsDown()
    {
        DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage listening = await client.GetAsync(
            DriverEndpoints.Events,
            HttpCompletionOption.ResponseHeadersRead,
            Soon()
        );

        Assert.Equal(HttpStatusCode.OK, listening.StatusCode);

        Stream body = await listening.Content.ReadAsStreamAsync(Soon());
        Task<string> reading = new StreamReader(body).ReadToEndAsync(Soon());

        await driver.DisposeAsync();

        string heard = await reading.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Contains("event: draining", heard);
    }

    [Fact]
    public async Task HealthSaysWhetherTheDriverIsDraining()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage before = await client.GetAsync(DriverEndpoints.Health, Soon());
        DriverHello? serving = await DriverUnderTest.Read(before, DriverJson.Context.DriverHello);

        Assert.NotNull(serving);
        Assert.False(serving.Draining);

        driver.Service<TunerSessionManager>().EnterDraining();

        using HttpResponseMessage after = await client.GetAsync(DriverEndpoints.Health, Soon());
        DriverHello? draining = await DriverUnderTest.Read(after, DriverJson.Context.DriverHello);

        Assert.NotNull(draining);
        Assert.True(draining.Draining);
    }

    [Fact]
    public async Task AStartedSessionIsCreatedAndThenListed()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("live-one")),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(
            DriverEndpoints.Session(SessionId.Parse("live-one")),
            created.Headers.Location?.ToString()
        );

        SessionSnapshot? snapshot = await DriverUnderTest.Read(created, DriverJson.Context.SessionSnapshot);

        Assert.NotNull(snapshot);
        Assert.Equal(SessionPurpose.Live, snapshot.Purpose);
        Assert.NotNull(snapshot.InstanceId);

        using HttpResponseMessage listed = await client.GetAsync(DriverEndpoints.Sessions, Soon());
        IReadOnlyList<SessionSnapshot>? sessions = await DriverUnderTest.Read(
            listed,
            DriverJson.Context.IReadOnlyListSessionSnapshot
        );

        Assert.NotNull(sessions);
        SessionSnapshot only = Assert.Single(sessions);
        Assert.Equal("live-one", only.SessionId.Value);
        Assert.NotNull(only.Counters);
    }

    [Fact]
    public async Task ASessionCarriesEnoughToJudgeTheCapture()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(
                DriverUnderTest.Recording("judged", DateTimeOffset.UtcNow.AddMinutes(5))
            ),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        IReadOnlyList<SessionSnapshot> body = await WaitUntil(
            client,
            sessions =>
                sessions.Single() is { BytesRecorded: > 0, Counters.Packets: > 0 }
        );

        SessionSnapshot only = body.Single();

        Assert.Equal(SessionState.Active, only.State);
        Assert.Equal(SessionStopReason.Running, only.StopReason);
        Assert.Equal(0, only.FaultCount);
        Assert.True(only.BytesRecorded > 0);
        Assert.NotNull(only.Counters);
        Assert.True(only.Counters.Packets > 0);
        Assert.Equal("primary", only.OutputRoot);
    }

    [Fact]
    public async Task TheFaultCountIsOnTheWire()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("counted")),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using HttpResponseMessage listed = await client.GetAsync(DriverEndpoints.Sessions, Soon());
        string raw = await listed.Content.ReadAsStringAsync(Soon());

        Assert.Contains("\"faultCount\":", raw, StringComparison.Ordinal);
        Assert.Contains("\"bytesRecorded\":", raw, StringComparison.Ordinal);
        Assert.Contains("\"stopReason\":", raw, StringComparison.Ordinal);
        Assert.Contains("\"counters\":", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDiagnosticsOfAnUntroubledDriverAreEmpty()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage response = await client.GetAsync(DriverEndpoints.Diagnostics, Soon());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IReadOnlyList<DiagnosticSnapshot>? entries = await DriverUnderTest.Read(
            response,
            DriverJson.Context.IReadOnlyListDiagnosticSnapshot
        );

        Assert.NotNull(entries);
        Assert.Empty(entries);
    }

    [Fact]
    public async Task AWriteFailureIsDiagnosedMarkedFailedAndDoesNotKillTheDriver()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start(reshapeServices: services =>
            services.AddSingleton<IRecordingWriterFactory>(new BrittleRecordingWriterFactory())
        );
        using HttpClient client = driver.Client();

        using HttpResponseMessage created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(
                DriverUnderTest.Recording("starved", DateTimeOffset.UtcNow.AddMinutes(5))
            ),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        IReadOnlyList<SessionSnapshot> settled = await WaitUntil(
            client,
            sessions => sessions.Single().State is SessionState.Failed
        );

        Assert.Equal(SessionStopReason.RecordingFailed, settled.Single().StopReason);

        using HttpResponseMessage diagnosed = await client.GetAsync(DriverEndpoints.Diagnostics, Soon());
        IReadOnlyList<DiagnosticSnapshot>? entries = await DriverUnderTest.Read(
            diagnosed,
            DriverJson.Context.IReadOnlyListDiagnosticSnapshot
        );

        Assert.NotNull(entries);

        DiagnosticSnapshot entry = Assert.Single(
            entries,
            candidate => candidate.Reason is DiagnosticReason.RecordingWriteFailed
        );

        Assert.Equal("starved", entry.SessionId.Value);
        Assert.Equal("fake-terrestrial", entry.DeviceId);
        Assert.Contains("No space left on device", entry.Detail, StringComparison.Ordinal);

        using HttpResponseMessage onward = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("onward")),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Created, onward.StatusCode);

        using HttpResponseMessage health = await client.GetAsync(DriverEndpoints.Health, Soon());

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task ADeviceFailureFaultsOnlyThatTunerOnTheWire()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start(reshapeServices: services =>
            services.AddSingleton<ITunerDeviceFactory>(
                new SelectiveTunerDeviceFactory("fake-terrestrial")
            )
        );
        using HttpClient client = driver.Client();

        using HttpResponseMessage created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("doomed", "fake-terrestrial")),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        await WaitUntil(client, sessions => sessions.Single().State is SessionState.Failed);

        using HttpResponseMessage listed = await client.GetAsync(DriverEndpoints.Tuners, Soon());
        IReadOnlyList<TunerSnapshot>? tuners = await DriverUnderTest.Read(
            listed,
            DriverJson.Context.IReadOnlyListTunerSnapshot
        );

        Assert.NotNull(tuners);

        TunerSnapshot faulted = tuners.Single(tuner => tuner.DeviceId == "fake-terrestrial");

        Assert.Equal(TunerState.Faulted, faulted.State);
        Assert.NotNull(faulted.Detail);
        Assert.Equal(
            TunerState.Idle,
            tuners.Single(tuner => tuner.DeviceId == "fake-satellite").State
        );

        using HttpResponseMessage refused = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("again", "fake-terrestrial")),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        DriverProblem? problem = await DriverUnderTest.Read(refused, DriverJson.Context.DriverProblem);

        Assert.NotNull(problem);
        Assert.Equal("faultedDevice", problem.Title);

        using HttpResponseMessage satellite = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(
                new StartSessionRequest
                {
                    SessionId = SessionId.Parse("sideways"),
                    Purpose = SessionPurpose.Live,
                    Tuning = new TuningRequest(TunerKind.Satellite, 23),
                }
            ),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Created, satellite.StatusCode);
    }

    [Fact]
    public async Task ABodyThatIsNotJsonIsRefusedWithAReason()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using var content = new StringContent("{ not json", Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using HttpResponseMessage response = await client.PostAsync(DriverEndpoints.Sessions, content, Soon());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        DriverProblem? problem = await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem);

        Assert.NotNull(problem);
        Assert.Equal("malformedRequest", problem.Title);
        Assert.NotEmpty(problem.Problems);
    }

    [Fact]
    public async Task ARequestThatBreaksTheRulesIsRefusedWithEveryReason()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using var content = new StringContent(
            """{"sessionId":"bad-one","purpose":"recording","tuning":{"kind":"terrestrial","physicalChannel":999}}""",
            Encoding.UTF8,
            "application/json"
        );

        using HttpResponseMessage response = await client.PostAsync(DriverEndpoints.Sessions, content, Soon());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        DriverProblem? problem = await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem);

        Assert.NotNull(problem);
        Assert.Equal("rejected", problem.Title);
        Assert.Contains(
            problem.Problems,
            entry => entry.Contains("physicalChannel", StringComparison.Ordinal)
        );
    }

    [Theory]
    [InlineData("no-such-device", HttpStatusCode.BadRequest, "unknownDevice")]
    [InlineData("fake-spare", HttpStatusCode.Conflict, "disabledDevice")]
    [InlineData("fake-satellite", HttpStatusCode.BadRequest, "wrongDeviceKind")]
    public async Task ARefusedSessionSaysWhichRefusalItWas(
        string deviceId,
        HttpStatusCode status,
        string title
    )
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage response = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("refused", deviceId)),
            Soon()
        );

        Assert.Equal(status, response.StatusCode);

        DriverProblem? problem = await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem);

        Assert.NotNull(problem);
        Assert.Equal(title, problem.Title);
    }

    [Fact]
    public async Task TheSameSessionIsNotStartedTwice()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage first = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("twice")),
            Soon()
        );
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using HttpResponseMessage second = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("twice")),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        DriverProblem? problem = await DriverUnderTest.Read(second, DriverJson.Context.DriverProblem);

        Assert.NotNull(problem);
        Assert.Equal("duplicateSession", problem.Title);
    }

    [Fact]
    public async Task ARecordingThatNamesAnUnknownRootIsRefused()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage response = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(
                DriverUnderTest.Recording("elsewhere", DateTimeOffset.UtcNow.AddMinutes(5), "nowhere")
            ),
            Soon()
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        DriverProblem? problem = await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem);

        Assert.NotNull(problem);
        Assert.Equal("unknownOutputRoot", problem.Title);
    }

    [Fact]
    public async Task StoppingASessionIsAnsweredOnceItHasLetGoAndThenSaysItIsDone()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("stopped")),
            Soon()
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        string path = $"{DriverEndpoints.Session(SessionId.Parse("stopped"))}?reason=test";

        using HttpResponseMessage stopping = await client.DeleteAsync(path, Soon());
        Assert.Equal(HttpStatusCode.OK, stopping.StatusCode);

        await WaitUntil(client, sessions => sessions.Single().Concluded);

        using HttpResponseMessage again = await client.DeleteAsync(path, Soon());

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        SessionSnapshot? snapshot = await DriverUnderTest.Read(again, DriverJson.Context.SessionSnapshot);

        Assert.NotNull(snapshot);
        Assert.True(snapshot.Concluded);
        Assert.Equal(SessionStopReason.Requested, snapshot.StopReason);
    }

    [Fact]
    public async Task StoppingASessionThatCannotLetGoInTimeSaysItIsStillStopping()
    {
        var device = new HeldOpenTunerDevice();

        await using DriverUnderTest driver = await DriverUnderTest.Start(reshapeServices: services =>
        {
            services.AddSingleton<ITunerDeviceFactory>(new OneTunerDeviceFactory(device));
            services.AddSingleton(provider => new TunerSessionManager(
                provider.GetRequiredService<DriverConfiguration>(),
                provider.GetRequiredService<ITunerDeviceFactory>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<TunerSessionManager>>(),
                events: provider.GetRequiredService<DriverEventHub>(),
                letGoLimit: TimeSpan.FromMilliseconds(50)
            ));
        });
        using HttpClient client = driver.Client();

        using HttpResponseMessage created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("stuck")),
            Soon()
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.True(
            device.Reading.Wait(Patience),
            "The session never reached the read that cannot be interrupted."
        );

        using HttpResponseMessage stopping = await client.DeleteAsync(
            $"{DriverEndpoints.Session(SessionId.Parse("stuck"))}?reason=test",
            Soon()
        );

        Assert.Equal(HttpStatusCode.Accepted, stopping.StatusCode);

        SessionSnapshot? snapshot = await DriverUnderTest.Read(stopping, DriverJson.Context.SessionSnapshot);

        Assert.NotNull(snapshot);
        Assert.NotEqual(SessionState.Stopped, snapshot.State);

        device.LetGo();

        await WaitUntil(client, sessions => sessions.Single().Concluded);
    }

    [Fact]
    public async Task AFrontendThatDidNotLockAnswersWithItsOwnProblemName()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start(reshapeServices: services =>
            services.AddSingleton<ITunerDeviceFactory>(new NoLockDeviceFactory())
        );
        using HttpClient client = driver.Client();

        using HttpResponseMessage refused = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("empty-channel")),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        DriverProblem? problem = await DriverUnderTest.Read(refused, DriverJson.Context.DriverProblem);

        Assert.NotNull(problem);
        Assert.Equal("noLock", problem.Title);
        Assert.Contains(
            "did not lock",
            string.Join(" ", problem.Problems),
            StringComparison.Ordinal
        );
    }

    private sealed class NoLockDeviceFactory : ITunerDeviceFactory
    {
        public ITunerDevice Create(
            DeviceSettings device,
            TuningRequest tuning,
            TuneParams? tune
        ) =>
            throw Tuning.Dvb.DvbFailure.NoLock(
                "/dev/dvb/adapter0/frontend0: the frontend did not lock within 5 seconds,"
                + " and the last status it reported while waiting was None."
            );
    }

    [Fact]
    public async Task StoppingASessionWithoutSayingWhyIsRefused()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("unexplained")),
            Soon()
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using HttpResponseMessage response = await client.DeleteAsync(
            DriverEndpoints.Session(SessionId.Parse("unexplained")),
            Soon()
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        DriverProblem? problem = await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem);

        Assert.NotNull(problem);
        Assert.Equal("reasonRequired", problem.Title);
    }

    [Fact]
    public async Task StoppingASessionTellsWhoeverIsWatching()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("watched")),
            Soon()
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        DriverEventHub hub = driver.Service<DriverEventHub>();

        Assert.True(hub.TryListen(out DriverEventListener? listener));

        using (listener)
        {
            using HttpResponseMessage stopped = await client.DeleteAsync(
                $"{DriverEndpoints.Session(SessionId.Parse("watched"))}?reason=test",
                Soon()
            );

            Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);

            IReadOnlyList<string> taken = await listener.Take(
                new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token
            );

            Assert.Contains(DriverEvents.SessionStopRequested, taken);
        }
    }

    [Fact]
    public async Task StoppingASessionThatIsNotThereIsNotFound()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage response = await client.DeleteAsync(
            $"{DriverEndpoints.Session(SessionId.Parse("absent"))}?reason=test",
            Soon()
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        DriverProblem? problem = await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem);

        Assert.NotNull(problem);
        Assert.Equal("noSuchSession", problem.Title);
    }

    [Fact]
    public async Task ASessionIdTheDriverCannotReadIsRefused()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage response = await client.DeleteAsync($"{DriverEndpoints.Sessions}/not_valid", Soon());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        DriverProblem? problem = await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem);

        Assert.NotNull(problem);
        Assert.Equal("badSessionId", problem.Title);
    }

    [Fact]
    public async Task AStreamCarriesRawTransportStream()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("watched")),
            Soon()
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            DriverEndpoints.SessionStream(SessionId.Parse("watched"))
        );

        using HttpResponseMessage response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            Soon()
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(SessionStreamHandler.ContentType, response.Content.Headers.ContentType?.MediaType);

        await using Stream body = await response.Content.ReadAsStreamAsync(Soon());

        byte[] buffer = new byte[TsPacketLength * 4];
        await body.ReadExactlyAsync(buffer, Soon());

        Assert.Equal(0x47, buffer[0]);
        Assert.Equal(0x47, buffer[TsPacketLength]);
    }

    [Fact]
    public async Task AStreamThatEndsCleanlyReadsToTheEnd()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("clean")),
            Soon()
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            DriverEndpoints.SessionStream(SessionId.Parse("clean"))
        );

        using HttpResponseMessage response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            Soon()
        );

        await using Stream body = await response.Content.ReadAsStreamAsync(Soon());

        byte[] buffer = new byte[TsPacketLength];
        await body.ReadExactlyAsync(buffer, Soon());

        using HttpClient stopper = driver.Client();
        using HttpResponseMessage stopped = await stopper.DeleteAsync(
            $"{DriverEndpoints.Session(SessionId.Parse("clean"))}?reason=test",
            Soon()
        );
        Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);

        await using var sink = new MemoryStream();
        await body.CopyToAsync(sink, Soon());
    }

    [Fact]
    public async Task AStreamThatFailedMidwayNeverReadsAsAFinishedOne()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("severed")),
            Soon()
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            DriverEndpoints.SessionStream(SessionId.Parse("severed"))
        );

        using HttpResponseMessage response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            Soon()
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using Stream body = await response.Content.ReadAsStreamAsync(Soon());

        byte[] buffer = new byte[TsPacketLength];
        await body.ReadExactlyAsync(buffer, Soon());

        TunerSessionManager manager = driver.Service<TunerSessionManager>();
        Assert.True(manager.TryGet(SessionId.Parse("severed"), out TunerSession? session));

        session.Broadcaster.Close(new IOException("the tuning was lost"));

        await using var sink = new MemoryStream();

        await Assert.ThrowsAnyAsync<IOException>(
            async () => await body.CopyToAsync(sink, Soon())
        );
    }

    [Fact]
    public async Task AStreamForASessionThatIsNotThereIsNotFound()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage response = await client.GetAsync(
            DriverEndpoints.SessionStream(SessionId.Parse("absent")),
            Soon()
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AStreamAskedForAsSomethingUnknownIsRefused()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("kinded")),
            Soon()
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        string path = DriverEndpoints.SessionStream(SessionId.Parse("kinded"));

        using HttpResponseMessage response = await client.GetAsync(
            $"{path}?{DriverEndpoints.SubscriberQuery}=cameraman",
            Soon()
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        DriverProblem? problem = await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem);

        Assert.NotNull(problem);
        Assert.Equal("unknownSubscriber", problem.Title);
    }

    [Fact]
    public async Task AStreamForASessionThatHasEndedIsRefused()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("over")),
            Soon()
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using HttpResponseMessage stopped = await client.DeleteAsync(
            $"{DriverEndpoints.Session(SessionId.Parse("over"))}?reason=test",
            Soon()
        );
        Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);

        await WaitUntil(client, sessions => sessions.Single().Concluded);

        using HttpResponseMessage response = await client.GetAsync(
            DriverEndpoints.SessionStream(SessionId.Parse("over")),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        DriverProblem? problem = await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem);

        Assert.NotNull(problem);
        Assert.Equal("sessionEnded", problem.Title);

        string detail = Assert.Single(problem.Problems);

        Assert.Contains("(requested)", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EventsArriveAsNamedSignalsWithNoPayload()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient listener = driver.Client();

        using var request = new HttpRequestMessage(HttpMethod.Get, DriverEndpoints.Events);
        using HttpResponseMessage response = await listener.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            Soon()
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(DriverEventStream.ContentType, response.Content.Headers.ContentType?.MediaType);

        await using Stream body = await response.Content.ReadAsStreamAsync(Soon());
        using var reader = new StreamReader(body, Encoding.UTF8);

        using HttpClient starter = driver.Client();
        using HttpResponseMessage created = await starter.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("announced")),
            Soon()
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var names = new List<string>();
        CancellationToken token = Soon();

        while (names.Count is 0 && await reader.ReadLineAsync(token) is { } line)
        {
            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                names.Add(line["event: ".Length..]);
            }
        }

        Assert.NotEmpty(names);
        Assert.All(names, name => Assert.Contains(name, DriverEvents.All));
    }

    [Fact]
    public async Task AListenerTooManyIsTurnedAwayPolitely()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();

        var clients = new List<HttpClient>();
        var responses = new List<HttpResponseMessage>();

        try
        {
            for (int taken = 0; taken < DriverEventHub.DefaultListenerLimit; taken++)
            {
                HttpClient client = driver.Client();
                clients.Add(client);

                var request = new HttpRequestMessage(HttpMethod.Get, DriverEndpoints.Events);
                HttpResponseMessage response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    Soon()
                );
                responses.Add(response);

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            using HttpClient late = driver.Client();
            using HttpResponseMessage refused = await late.GetAsync(DriverEndpoints.Events, Soon());

            Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);

            DriverProblem? problem = await DriverUnderTest.Read(refused, DriverJson.Context.DriverProblem);

            Assert.NotNull(problem);
            Assert.Equal("tooManyListeners", problem.Title);
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }

            foreach (HttpClient client in clients)
            {
                client.Dispose();
            }
        }
    }

    [Fact]
    public async Task APathTheDriverDoesNotServeIsNotFound()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage response = await client.GetAsync("/something-else", Soon());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AMethodTheDriverDoesNotServeIsNotAllowed()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage response = await client.PostAsync(
            DriverEndpoints.Health,
            new StringContent(string.Empty),
            Soon()
        );

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    private const int TsPacketLength = 188;

    private static async Task TheStreamEnds(Stream body, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[TsPacketLength];

        try
        {
            while (await body.ReadAsync(buffer, cancellationToken) > 0)
            { }
        }
        catch (IOException)
        { }
    }

    private static async Task Until(Func<bool> fact, string otherwise)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + Patience;

        while (!fact())
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                Assert.Fail(otherwise);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }

    private static async Task<IReadOnlyList<SessionSnapshot>> WaitUntil(
        HttpClient client,
        Func<IReadOnlyList<SessionSnapshot>, bool> settled
    )
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + Patience;

        while (true)
        {
            using HttpResponseMessage response = await client.GetAsync(DriverEndpoints.Sessions, Soon());
            IReadOnlyList<SessionSnapshot>? sessions = await DriverUnderTest.Read(
                response,
                DriverJson.Context.IReadOnlyListSessionSnapshot
            );

            if (sessions is { Count: > 0 } && settled(sessions))
            {
                return sessions;
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                Assert.Fail("The driver never reached the state the test was waiting for.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }
}
