using System.Net;
using System.Net.Http.Json;
using System.Text;

using Carina.Contracts;

namespace Carina.Driver.Tests;

public sealed class RecordingExtensionApiTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static CancellationToken Soon() => new CancellationTokenSource(Patience).Token;

    private static HttpContent Body(DateTimeOffset endsAt) =>
        JsonContent.Create(
            new ExtendSessionRequest { EndsAt = endsAt },
            DriverJson.Context.ExtendSessionRequest
        );

    private static async Task<RecordingUnderTest> Recording(string sessionId)
    {
        DriverUnderTest driver = await DriverUnderTest.Start();
        HttpClient client = driver.Client();
        DateTimeOffset endsAt = DateTimeOffset.UtcNow.AddMinutes(30);

        using HttpResponseMessage created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Recording(sessionId, endsAt)),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        return new RecordingUnderTest(driver, client, sessionId, endsAt);
    }

    private static async Task<DriverProblem?> ProblemIn(HttpResponseMessage response) =>
        await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem);

    [Fact]
    public async Task AProgrammeThatRunsLateMovesTheEndOfTheRecordingLater()
    {
        await using RecordingUnderTest recording = await Recording("late");
        DateTimeOffset later = recording.EndsAt.AddMinutes(10);

        using HttpResponseMessage patched = await recording.Patch(later);

        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);

        SessionSnapshot? snapshot = await DriverUnderTest.Read(
            patched,
            DriverJson.Context.SessionSnapshot
        );

        Assert.NotNull(snapshot);
        Assert.Equal(later, snapshot.EndsAt);
        Assert.Equal(later, await recording.CurrentEnd());
    }

    [Fact]
    public async Task AnEndAtTheVeryTimeTheRecordingAlreadyStopsIsNotAnExtension()
    {
        await using RecordingUnderTest recording = await Recording("same");

        using HttpResponseMessage patched = await recording.Patch(recording.EndsAt);

        Assert.Equal(HttpStatusCode.BadRequest, patched.StatusCode);
        Assert.Equal(SessionRefusalTitles.NotAnExtension, (await ProblemIn(patched))?.Title);
        Assert.Equal(recording.EndsAt, await recording.CurrentEnd());
    }

    [Fact]
    public async Task AnEndEarlierThanTheOneTheRecordingHasIsNotAnExtension()
    {
        await using RecordingUnderTest recording = await Recording("short");

        using HttpResponseMessage patched = await recording.Patch(
            recording.EndsAt.AddMinutes(-10)
        );

        Assert.Equal(HttpStatusCode.BadRequest, patched.StatusCode);
        Assert.Equal(SessionRefusalTitles.NotAnExtension, (await ProblemIn(patched))?.Title);
        Assert.Equal(recording.EndsAt, await recording.CurrentEnd());
    }

    [Fact]
    public async Task TheRefusalOfAShorteningIsNotARefusalOfEveryChange()
    {
        await using RecordingUnderTest recording = await Recording("both");

        using (HttpResponseMessage refused = await recording.Patch(recording.EndsAt.AddMinutes(-5)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }

        DateTimeOffset later = recording.EndsAt.AddMinutes(5);

        using HttpResponseMessage accepted = await recording.Patch(later);

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(later, await recording.CurrentEnd());
    }

    [Fact]
    public async Task ABodyThatIsNotAnExtensionRequestIsRefusedWithoutTouchingTheSession()
    {
        await using RecordingUnderTest recording = await Recording("garbled");

        using var body = new StringContent("{ not json", Encoding.UTF8, "application/json");

        using HttpResponseMessage patched = await recording.PatchWith(body);

        Assert.Equal(HttpStatusCode.BadRequest, patched.StatusCode);
        Assert.Equal("malformedRequest", (await ProblemIn(patched))?.Title);
        Assert.Equal(recording.EndsAt, await recording.CurrentEnd());
    }

    [Fact]
    public async Task ARecordingThatHasAlreadyEndedIsNotMadeLonger()
    {
        await using RecordingUnderTest recording = await Recording("over");

        using (HttpResponseMessage stopped = await recording.Stop())
        {
            Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);
        }

        using HttpResponseMessage patched = await recording.Patch(recording.EndsAt.AddHours(1));

        Assert.Equal(HttpStatusCode.Conflict, patched.StatusCode);
        Assert.Equal(SessionRefusalTitles.SessionEnded, (await ProblemIn(patched))?.Title);
    }

    [Fact]
    public async Task ASessionThatWritesNoFileIsNotHeldToAProgrammeAndIsNotExtended()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("watching")),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        DateTimeOffset? before = await RecordingUnderTest.EndOf(client, "watching");

        using HttpResponseMessage patched = await client.PatchAsync(
            DriverEndpoints.Session(SessionId.Parse("watching")),
            Body(DateTimeOffset.UtcNow.AddHours(9)),
            Soon()
        );

        Assert.Equal(HttpStatusCode.BadRequest, patched.StatusCode);
        Assert.Equal(SessionRefusalTitles.NotARecording, (await ProblemIn(patched))?.Title);
        Assert.Equal(before, await RecordingUnderTest.EndOf(client, "watching"));
    }

    [Fact]
    public async Task ASessionThisDriverNeverHeldHasNoEndToMove()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage patched = await client.PatchAsync(
            DriverEndpoints.Session(SessionId.Parse("nobody")),
            Body(DateTimeOffset.UtcNow.AddHours(1)),
            Soon()
        );

        Assert.Equal(HttpStatusCode.NotFound, patched.StatusCode);
        Assert.Equal("noSuchSession", (await ProblemIn(patched))?.Title);
    }

    [Fact]
    public async Task ADriverThatFollowsAProgrammeSaysSoInItsGreeting()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage response = await client.GetAsync(DriverEndpoints.Health, Soon());

        DriverHello? hello = await DriverUnderTest.Read(response, DriverJson.Context.DriverHello);

        Assert.NotNull(hello);
        Assert.True(hello.Supports(DriverCapabilities.RecordingExtension));
    }

    private sealed class RecordingUnderTest(
        DriverUnderTest driver,
        HttpClient client,
        string sessionId,
        DateTimeOffset endsAt
    ) : IAsyncDisposable
    {
        public DateTimeOffset EndsAt { get; } = endsAt;

        public Task<HttpResponseMessage> Patch(DateTimeOffset to) => PatchWith(Body(to));

        public async Task<HttpResponseMessage> PatchWith(HttpContent body) =>
            await client.PatchAsync(
                DriverEndpoints.Session(SessionId.Parse(sessionId)),
                body,
                Soon()
            );

        public async Task<HttpResponseMessage> Stop() =>
            await client.DeleteAsync(
                $"{DriverEndpoints.Session(SessionId.Parse(sessionId))}?reason=the test is over",
                Soon()
            );

        public Task<DateTimeOffset?> CurrentEnd() => EndOf(client, sessionId);

        public static async Task<DateTimeOffset?> EndOf(HttpClient client, string sessionId)
        {
            using HttpResponseMessage response = await client.GetAsync(
                DriverEndpoints.Session(SessionId.Parse(sessionId)),
                Soon()
            );

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            SessionSnapshot? snapshot = await DriverUnderTest.Read(
                response,
                DriverJson.Context.SessionSnapshot
            );

            Assert.NotNull(snapshot);

            return snapshot.EndsAt;
        }

        public async ValueTask DisposeAsync()
        {
            using (await Stop())
            {
            }

            client.Dispose();

            await driver.DisposeAsync();
        }
    }
}
