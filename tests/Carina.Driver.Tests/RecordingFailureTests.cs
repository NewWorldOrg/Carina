using System.Net;

using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Recording;
using Carina.Driver.Sessions;
using Carina.Driver.Tuning;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Driver.Tests;

public sealed class RecordingFailureTests : IDisposable
{
    private const int ARoomThatRunsOutMidChunk = TunerSession.DefaultChunkSize + 500;

    private static readonly DateTimeOffset Start = new(2026, 8, 13, 21, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan Deadlock = TimeSpan.FromSeconds(30);

    private readonly string root = Directory.CreateTempSubdirectory("carina-failure-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    private static CancellationToken Soon() => new CancellationTokenSource(Patience).Token;

    private DriverConfiguration Configuration =>
        new(
            "/run/carina/driver.sock",
            [new OutputRootSettings("primary", root)],
            6,
            new TunerSettings(TunerBackend.Fake),
            [new DeviceSettings("adapter0", DeviceKind.Terrestrial)]
        );

    private static StartSessionRequest Recording(string sessionId, DateTimeOffset endsAt) =>
        new()
        {
            SessionId = SessionId.Parse(sessionId),
            Purpose = SessionPurpose.Recording,
            Tuning = new TuningRequest(TunerKind.Terrestrial, 55, 50001),
            DeviceId = "adapter0",
            OutputRoot = "primary",
            RecordingId = $"k-{sessionId}",
            EndsAt = endsAt,
        };

    private static async Task<IReadOnlyList<SessionSnapshot>> Settled(
        HttpClient client,
        Func<IReadOnlyList<SessionSnapshot>, bool> until
    )
    {
        DateTime deadline = DateTime.UtcNow + Patience;

        while (DateTime.UtcNow < deadline)
        {
            using HttpResponseMessage response = await client.GetAsync(
                DriverEndpoints.Sessions,
                Soon()
            );

            IReadOnlyList<SessionSnapshot>? sessions = await DriverUnderTest.Read(
                response,
                DriverJson.Context.IReadOnlyListSessionSnapshot
            );

            if (sessions is not null && sessions.Count > 0 && until(sessions))
            {
                return sessions;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        Assert.Fail("The driver never reached the state the test was waiting for.");

        return [];
    }

    [Fact]
    public async Task ARecordingThatRanOutOfRoomLeavesOneStubAndIsNotOpenedAgain()
    {
        var writers = new RationedRecordingWriterFactory(ARoomThatRunsOutMidChunk);

        await using DriverUnderTest driver = await DriverUnderTest.Start(
            reshapeServices: services => services.AddSingleton<IRecordingWriterFactory>(writers)
        );
        using HttpClient client = driver.Client();
        string output = driver.Configuration.OutputRoots!.Single().Path!;

        using (HttpResponseMessage created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(
                DriverUnderTest.Recording("starved", DateTimeOffset.UtcNow.AddMinutes(30))
            ),
            Soon()
        ))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        IReadOnlyList<SessionSnapshot> settled = await Settled(
            client,
            sessions => sessions.Single().State is SessionState.Failed
        );

        Assert.Equal(SessionStopReason.RecordingFailed, settled.Single().StopReason);
        Assert.Equal(0, settled.Single().FaultCount);
        Assert.Equal(1, writers.Opened);

        string file = Assert.Single(Directory.GetFiles(output));

        Assert.Equal(Path.Combine(output, "k-starved.ts"), file);
        Assert.Equal(ARoomThatRunsOutMidChunk, new FileInfo(file).Length);
        Assert.Equal(new FileInfo(file).Length, settled.Single().BytesRecorded);
    }

    [Fact]
    public async Task BRKD010_TheReasonARecordingRanOutOfRoomNamesNoDirectoryOnTheHost()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();
        string output = driver.Configuration.OutputRoots!.Single().Path!;

        File.CreateSymbolicLink(Path.Combine(output, "k-starved.ts"), "/dev/full");

        using (HttpResponseMessage created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(
                DriverUnderTest.Recording("starved", DateTimeOffset.UtcNow.AddMinutes(30))
            ),
            Soon()
        ))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        IReadOnlyList<SessionSnapshot> settled = await Settled(
            client,
            sessions => sessions.Single().State is SessionState.Failed
        );

        SessionSnapshot starved = settled.Single();

        Assert.Equal(SessionStopReason.RecordingFailed, starved.StopReason);
        Assert.NotNull(starved.FailureCause);
        Assert.Contains("No space left on device", starved.FailureCause, StringComparison.Ordinal);
        Assert.Contains("k-starved.ts", starved.FailureCause, StringComparison.Ordinal);
        Assert.DoesNotContain(output, starved.FailureCause, StringComparison.Ordinal);

        using HttpResponseMessage diagnosed = await client.GetAsync(
            DriverEndpoints.Diagnostics,
            Soon()
        );

        IReadOnlyList<DiagnosticSnapshot>? entries = await DriverUnderTest.Read(
            diagnosed,
            DriverJson.Context.IReadOnlyListDiagnosticSnapshot
        );

        Assert.NotNull(entries);

        DiagnosticSnapshot entry = Assert.Single(
            entries,
            candidate => candidate.Reason is DiagnosticReason.RecordingWriteFailed
        );

        Assert.NotNull(entry.Detail);
        Assert.DoesNotContain(output, entry.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARecordingThatRanOutOfRoomSaysWhySoAndLeavesTheDriverServing()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start(
            reshapeServices: services =>
                services.AddSingleton<IRecordingWriterFactory>(
                    new RationedRecordingWriterFactory(ARoomThatRunsOutMidChunk)
                )
        );
        using HttpClient client = driver.Client();

        using (HttpResponseMessage created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(
                DriverUnderTest.Recording("starved", DateTimeOffset.UtcNow.AddMinutes(30))
            ),
            Soon()
        ))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        await Settled(client, sessions => sessions.Single().State is SessionState.Failed);

        using HttpResponseMessage diagnosed = await client.GetAsync(
            DriverEndpoints.Diagnostics,
            Soon()
        );

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
        Assert.Contains("No space left on device", entry.Detail, StringComparison.Ordinal);

        using HttpResponseMessage onward = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("onward")),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Created, onward.StatusCode);

        using HttpResponseMessage stopped = await client.DeleteAsync(
            $"{DriverEndpoints.Session(SessionId.Parse("onward"))}?reason=the test is over",
            Soon()
        );

        Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("%20%20")]
    public async Task AStopThatSaysNothingLeavesTheRecordingRunning(string reason)
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using (HttpResponseMessage created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(
                DriverUnderTest.Recording("unexplained", DateTimeOffset.UtcNow.AddMinutes(30))
            ),
            Soon()
        ))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        using (HttpResponseMessage refused = await client.DeleteAsync(
            $"{DriverEndpoints.Session(SessionId.Parse("unexplained"))}?reason={reason}",
            Soon()
        ))
        {
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

            DriverProblem? problem = await DriverUnderTest.Read(
                refused,
                DriverJson.Context.DriverProblem
            );

            Assert.Equal("reasonRequired", problem?.Title);
        }

        using HttpResponseMessage listed = await client.GetAsync(
            DriverEndpoints.Session(SessionId.Parse("unexplained")),
            Soon()
        );

        SessionSnapshot? snapshot = await DriverUnderTest.Read(
            listed,
            DriverJson.Context.SessionSnapshot
        );

        Assert.NotNull(snapshot);
        Assert.Equal(SessionState.Active, snapshot.State);
        Assert.False(snapshot.Concluded);
        Assert.Equal(SessionStopReason.Running, snapshot.StopReason);

        using HttpResponseMessage stopped = await client.DeleteAsync(
            $"{DriverEndpoints.Session(SessionId.Parse("unexplained"))}?reason=the test is over",
            Soon()
        );

        Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);
    }

    [Fact]
    public async Task ARecordingIsWaitedForToTheGraceCapAndThenMarkedFailed()
    {
        var clock = new SteppedTimeProvider(Start);
        var device = new PacedTunerDevice();
        var manager = new TunerSessionManager(
            Configuration,
            new OneTunerDeviceFactory(device),
            clock,
            NullLogger<TunerSessionManager>.Instance
        );

        SessionStart start = manager.Begin(Recording("s-1", Start.AddHours(9)));

        Assert.True(start.TryGetSession(out TunerSession? recording), start.Detail);

        device.AwaitParkedBefore(1);

        Task draining = manager.DrainAsync(CancellationToken.None);

        clock.AwaitSomethingWaitingOnTheClock(Deadlock);
        clock.Advance(TimeSpan.FromHours(6) - TimeSpan.FromMinutes(1));

        Assert.False(
            draining.IsCompleted,
            "The driver stopped waiting before the grace it promised was up."
        );
        Assert.Equal(SessionState.Active, recording.State);

        clock.Advance(TimeSpan.FromMinutes(2));

        await draining;

        Assert.Equal(SessionState.Failed, recording.State);
        Assert.Equal(SessionStopReason.DrainCapReached, recording.StopReason);
        Assert.NotNull(recording.FailureCause);
    }

    [Fact]
    public async Task ARecordingThatFinishesWithinTheGraceIsNotMarkedFailed()
    {
        var clock = new SteppedTimeProvider(Start);
        var device = new PacedTunerDevice();
        var manager = new TunerSessionManager(
            Configuration,
            new OneTunerDeviceFactory(device),
            clock,
            NullLogger<TunerSessionManager>.Instance
        );

        SessionStart start = manager.Begin(Recording("s-1", Start.AddHours(9)));

        Assert.True(start.TryGetSession(out TunerSession? recording), start.Detail);

        device.AwaitParkedBefore(1);

        Task draining = manager.DrainAsync(CancellationToken.None);

        clock.AwaitSomethingWaitingOnTheClock(Deadlock);
        clock.Advance(TimeSpan.FromHours(1));
        recording.Stop();

        await draining;

        Assert.Equal(SessionState.Stopped, recording.State);
        Assert.Equal(SessionStopReason.Requested, recording.StopReason);
    }

    [Fact]
    public void TheGraceTheDriverPromisesIsTheOneItsConfigurationNames()
    {
        var manager = new TunerSessionManager(
            Configuration with { ShutdownGraceHours = 2 },
            new ScriptedTunerDeviceFactory(),
            new SteppedTimeProvider(Start),
            NullLogger<TunerSessionManager>.Instance,
            hardStopLimit: TimeSpan.FromSeconds(30)
        );

        Assert.Equal(TimeSpan.FromHours(2) + TimeSpan.FromSeconds(30), manager.ShutdownBudget);
    }
}
