using System.Net;
using System.Net.Http.Headers;
using System.Text;

using Carina.Contracts;
using Carina.Driver.Events;
using Carina.Driver.Ipc;
using Carina.Driver.Sessions;

namespace Carina.Driver.Tests;

public sealed class DriverApiTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static CancellationToken Soon() =>
        new CancellationTokenSource(Patience).Token;

    [Fact]
    public async Task HealthAnswersWithTheGreeting()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var response = await client.GetAsync(DriverEndpoints.Health, Soon());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var hello = await DriverUnderTest.Read(response, DriverJson.Context.DriverHello);

        Assert.NotNull(hello);
        Assert.Equal(DriverProtocol.Version, hello.ProtocolVersion);
        Assert.True(hello.Supports(DriverCapabilities.Recording));
        Assert.True(hello.Supports(DriverCapabilities.Live));
    }

    [Fact]
    public async Task TunersAnswerWithEveryDeclaredDevice()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var response = await client.GetAsync(DriverEndpoints.Tuners, Soon());
        var tuners = await DriverUnderTest.Read(
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
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var response = await client.GetAsync(DriverEndpoints.Sessions, Soon());
        var sessions = await DriverUnderTest.Read(
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
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("located-one")),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var fetched = await client.GetAsync(
            created.Headers.Location?.ToString(),
            Soon()
        );

        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);

        var snapshot = await DriverUnderTest.Read(fetched, DriverJson.Context.SessionSnapshot);

        Assert.NotNull(snapshot);
        Assert.Equal("located-one", snapshot.SessionId.Value);

        using var missing = await client.GetAsync(
            DriverEndpoints.Session(SessionId.Parse("nobody")),
            Soon()
        );

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task HealthSaysWhetherTheDriverIsDraining()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var before = await client.GetAsync(DriverEndpoints.Health, Soon());
        var serving = await DriverUnderTest.Read(before, DriverJson.Context.DriverHello);

        Assert.NotNull(serving);
        Assert.False(serving.Draining);

        driver.Service<TunerSessionManager>().EnterDraining();

        using var after = await client.GetAsync(DriverEndpoints.Health, Soon());
        var draining = await DriverUnderTest.Read(after, DriverJson.Context.DriverHello);

        Assert.NotNull(draining);
        Assert.True(draining.Draining);
    }

    [Fact]
    public async Task AStartedSessionIsCreatedAndThenListed()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("live-one")),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(
            DriverEndpoints.Session(SessionId.Parse("live-one")),
            created.Headers.Location?.ToString()
        );

        var snapshot = await DriverUnderTest.Read(created, DriverJson.Context.SessionSnapshot);

        Assert.NotNull(snapshot);
        Assert.Equal(SessionPurpose.Live, snapshot.Purpose);
        Assert.NotNull(snapshot.InstanceId);

        using var listed = await client.GetAsync(DriverEndpoints.Sessions, Soon());
        var sessions = await DriverUnderTest.Read(
            listed,
            DriverJson.Context.IReadOnlyListSessionSnapshot
        );

        Assert.NotNull(sessions);
        var only = Assert.Single(sessions);
        Assert.Equal("live-one", only.SessionId.Value);
        Assert.NotNull(only.Counters);
    }

    [Fact]
    public async Task ASessionCarriesEnoughToJudgeTheCapture()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(
                DriverUnderTest.Recording("judged", DateTimeOffset.UtcNow.AddMinutes(5))
            ),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var body = await WaitUntil(
            client,
            sessions => sessions.Single().BytesRecorded > 0
        );

        var only = body.Single();

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
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("counted")),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var listed = await client.GetAsync(DriverEndpoints.Sessions, Soon());
        var raw = await listed.Content.ReadAsStringAsync(Soon());

        Assert.Contains("\"faultCount\":", raw, StringComparison.Ordinal);
        Assert.Contains("\"bytesRecorded\":", raw, StringComparison.Ordinal);
        Assert.Contains("\"stopReason\":", raw, StringComparison.Ordinal);
        Assert.Contains("\"counters\":", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABodyThatIsNotJsonIsRefusedWithAReason()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var content = new StringContent("{ not json", Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await client.PostAsync(DriverEndpoints.Sessions, content, Soon());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem);

        Assert.NotNull(problem);
        Assert.Equal("malformedRequest", problem.Title);
        Assert.NotEmpty(problem.Problems);
    }

    [Fact]
    public async Task ARequestThatBreaksTheRulesIsRefusedWithEveryReason()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var content = new StringContent(
            """{"sessionId":"bad-one","purpose":"recording","tuning":{"kind":"terrestrial","physicalChannel":999}}""",
            Encoding.UTF8,
            "application/json"
        );

        using var response = await client.PostAsync(DriverEndpoints.Sessions, content, Soon());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem);

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
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var response = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("refused", deviceId)),
            Soon()
        );

        Assert.Equal(status, response.StatusCode);

        var problem = await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem);

        Assert.NotNull(problem);
        Assert.Equal(title, problem.Title);
    }

    [Fact]
    public async Task TheSameSessionIsNotStartedTwice()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var first = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("twice")),
            Soon()
        );
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var second = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("twice")),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var problem = await DriverUnderTest.Read(second, DriverJson.Context.DriverProblem);

        Assert.NotNull(problem);
        Assert.Equal("duplicateSession", problem.Title);
    }

    [Fact]
    public async Task ARecordingThatNamesAnUnknownRootIsRefused()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var response = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(
                DriverUnderTest.Recording("elsewhere", DateTimeOffset.UtcNow.AddMinutes(5), "nowhere")
            ),
            Soon()
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem);

        Assert.NotNull(problem);
        Assert.Equal("unknownOutputRoot", problem.Title);
    }

    [Fact]
    public async Task StoppingASessionIsAcceptedAndThenAlreadyDone()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("stopped")),
            Soon()
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var path = DriverEndpoints.Session(SessionId.Parse("stopped"));

        using var stopping = await client.DeleteAsync(path, Soon());
        Assert.Equal(HttpStatusCode.Accepted, stopping.StatusCode);

        await WaitUntil(client, sessions => sessions.Single().Concluded);

        using var again = await client.DeleteAsync(path, Soon());

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        var snapshot = await DriverUnderTest.Read(again, DriverJson.Context.SessionSnapshot);

        Assert.NotNull(snapshot);
        Assert.True(snapshot.Concluded);
        Assert.Equal(SessionStopReason.Requested, snapshot.StopReason);
    }

    [Fact]
    public async Task StoppingASessionThatIsNotThereIsNotFound()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var response = await client.DeleteAsync(
            DriverEndpoints.Session(SessionId.Parse("absent")),
            Soon()
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem);

        Assert.NotNull(problem);
        Assert.Equal("noSuchSession", problem.Title);
    }

    [Fact]
    public async Task ASessionIdTheDriverCannotReadIsRefused()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var response = await client.DeleteAsync($"{DriverEndpoints.Sessions}/not_valid", Soon());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem);

        Assert.NotNull(problem);
        Assert.Equal("badSessionId", problem.Title);
    }

    [Fact]
    public async Task AStreamCarriesRawTransportStream()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("watched")),
            Soon()
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            DriverEndpoints.SessionStream(SessionId.Parse("watched"))
        );

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            Soon()
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(SessionStreamHandler.ContentType, response.Content.Headers.ContentType?.MediaType);

        await using var body = await response.Content.ReadAsStreamAsync(Soon());

        var buffer = new byte[TsPacketLength * 4];
        await body.ReadExactlyAsync(buffer, Soon());

        Assert.Equal(0x47, buffer[0]);
        Assert.Equal(0x47, buffer[TsPacketLength]);
    }

    [Fact]
    public async Task AStreamThatEndsCleanlyReadsToTheEnd()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("clean")),
            Soon()
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            DriverEndpoints.SessionStream(SessionId.Parse("clean"))
        );

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            Soon()
        );

        await using var body = await response.Content.ReadAsStreamAsync(Soon());

        var buffer = new byte[TsPacketLength];
        await body.ReadExactlyAsync(buffer, Soon());

        using var stopper = driver.Client();
        using var stopped = await stopper.DeleteAsync(
            DriverEndpoints.Session(SessionId.Parse("clean")),
            Soon()
        );
        Assert.Equal(HttpStatusCode.Accepted, stopped.StatusCode);

        await using var sink = new MemoryStream();
        await body.CopyToAsync(sink, Soon());
    }

    [Fact]
    public async Task AStreamThatFailedMidwayNeverReadsAsAFinishedOne()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("severed")),
            Soon()
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            DriverEndpoints.SessionStream(SessionId.Parse("severed"))
        );

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            Soon()
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var body = await response.Content.ReadAsStreamAsync(Soon());

        var buffer = new byte[TsPacketLength];
        await body.ReadExactlyAsync(buffer, Soon());

        var manager = driver.Service<TunerSessionManager>();
        Assert.True(manager.TryGet(SessionId.Parse("severed"), out var session));

        session.Broadcaster.Close(new IOException("the tuning was lost"));

        await using var sink = new MemoryStream();

        await Assert.ThrowsAnyAsync<IOException>(
            async () => await body.CopyToAsync(sink, Soon())
        );
    }

    [Fact]
    public async Task AStreamForASessionThatIsNotThereIsNotFound()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var response = await client.GetAsync(
            DriverEndpoints.SessionStream(SessionId.Parse("absent")),
            Soon()
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AStreamAskedForAsSomethingUnknownIsRefused()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("kinded")),
            Soon()
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var path = DriverEndpoints.SessionStream(SessionId.Parse("kinded"));

        using var response = await client.GetAsync(
            $"{path}?{DriverEndpoints.SubscriberQuery}=cameraman",
            Soon()
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem);

        Assert.NotNull(problem);
        Assert.Equal("unknownSubscriber", problem.Title);
    }

    [Fact]
    public async Task AStreamForASessionThatHasEndedIsRefused()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("over")),
            Soon()
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var stopped = await client.DeleteAsync(
            DriverEndpoints.Session(SessionId.Parse("over")),
            Soon()
        );
        Assert.Equal(HttpStatusCode.Accepted, stopped.StatusCode);

        await WaitUntil(client, sessions => sessions.Single().Concluded);

        using var response = await client.GetAsync(
            DriverEndpoints.SessionStream(SessionId.Parse("over")),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem);

        Assert.NotNull(problem);
        Assert.Equal("sessionEnded", problem.Title);
    }

    [Fact]
    public async Task EventsArriveAsNamedSignalsWithNoPayload()
    {
        await using var driver = await DriverUnderTest.Start();
        using var listener = driver.Client();

        using var request = new HttpRequestMessage(HttpMethod.Get, DriverEndpoints.Events);
        using var response = await listener.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            Soon()
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(DriverEventStream.ContentType, response.Content.Headers.ContentType?.MediaType);

        await using var body = await response.Content.ReadAsStreamAsync(Soon());
        using var reader = new StreamReader(body, Encoding.UTF8);

        using var starter = driver.Client();
        using var created = await starter.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("announced")),
            Soon()
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var names = new List<string>();
        var token = Soon();

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
        await using var driver = await DriverUnderTest.Start();

        var clients = new List<HttpClient>();
        var responses = new List<HttpResponseMessage>();

        try
        {
            for (var taken = 0; taken < DriverEventHub.DefaultListenerLimit; taken++)
            {
                var client = driver.Client();
                clients.Add(client);

                var request = new HttpRequestMessage(HttpMethod.Get, DriverEndpoints.Events);
                var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    Soon()
                );
                responses.Add(response);

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            using var late = driver.Client();
            using var refused = await late.GetAsync(DriverEndpoints.Events, Soon());

            Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);

            var problem = await DriverUnderTest.Read(refused, DriverJson.Context.DriverProblem);

            Assert.NotNull(problem);
            Assert.Equal("tooManyListeners", problem.Title);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }

            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    [Fact]
    public async Task APathTheDriverDoesNotServeIsNotFound()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var response = await client.GetAsync("/something-else", Soon());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AMethodTheDriverDoesNotServeIsNotAllowed()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var response = await client.PostAsync(
            DriverEndpoints.Health,
            new StringContent(string.Empty),
            Soon()
        );

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    private const int TsPacketLength = 188;

    private static async Task<IReadOnlyList<SessionSnapshot>> WaitUntil(
        HttpClient client,
        Func<IReadOnlyList<SessionSnapshot>, bool> settled
    )
    {
        var deadline = DateTimeOffset.UtcNow + Patience;

        while (true)
        {
            using var response = await client.GetAsync(DriverEndpoints.Sessions, Soon());
            var sessions = await DriverUnderTest.Read(
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
