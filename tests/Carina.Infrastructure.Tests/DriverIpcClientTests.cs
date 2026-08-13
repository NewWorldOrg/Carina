using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Infrastructure.Configuration;
using Carina.Infrastructure.Driver;

using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Tests;

public sealed class DriverIpcClientTests
{
    private static string NewSocketPath()
        => Path.Combine(
            Directory.CreateTempSubdirectory("carina-ipc-").FullName,
            "driver.sock");

    private static DriverIpcClient ClientFor(string socketPath)
        => new(Options.Create(new DriverOptions { SocketPath = socketPath }));

    [Fact]
    public async Task ReadsTheDriversHello()
    {
        var socketPath = NewSocketPath();
        await using var driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));
        using var client = ClientFor(socketPath);

        var call = await client.GetHealthAsync(CancellationToken.None);

        Assert.True(call.TryGetValue(out var hello));
        Assert.Equal(DriverProtocol.Version, hello.ProtocolVersion);
        Assert.Equal("instance-a", hello.InstanceId);
        Assert.Equal(["recording", "live"], hello.Capabilities);
        Assert.Equal(200, call.StatusCode);
    }

    [Fact]
    public async Task ARefusalSurfacesTheProblemInsteadOfThrowing()
    {
        var socketPath = NewSocketPath();
        await using var driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));
        driver.RefuseEverythingWith = new DriverProblem("draining", ["The driver is shutting down."]);
        using var client = ClientFor(socketPath);

        var call = await client.GetHealthAsync(CancellationToken.None);

        Assert.Equal(DriverCallOutcome.Refused, call.Outcome);
        Assert.Equal(503, call.StatusCode);
        Assert.Equal("draining", call.Problem?.Title);
    }

    [Fact]
    public async Task AMissingSocketIsUnreachableNotAnException()
    {
        using var client = ClientFor(NewSocketPath());

        var call = await client.GetHealthAsync(CancellationToken.None);

        Assert.Equal(DriverCallOutcome.Unreachable, call.Outcome);
        Assert.NotNull(call.Failure);
    }

    [Fact]
    public async Task AStaleSocketFileIsUnreachableNotAnException()
    {
        var socketPath = NewSocketPath();
        await File.WriteAllTextAsync(socketPath, string.Empty, CancellationToken.None);
        using var client = ClientFor(socketPath);

        var call = await client.GetHealthAsync(CancellationToken.None);

        Assert.Equal(DriverCallOutcome.Unreachable, call.Outcome);
    }

    [Fact]
    public async Task ATruncatedBodyReadsAsUnreachableNotAsAnAnswer()
    {
        var socketPath = NewSocketPath();
        await using var driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));
        driver.TruncateHealth = true;
        using var client = ClientFor(socketPath);

        var call = await client.GetHealthAsync(CancellationToken.None);

        Assert.Equal(DriverCallOutcome.Unreachable, call.Outcome);
    }

    [Fact]
    public async Task ReadsTheActiveSessions()
    {
        var socketPath = NewSocketPath();
        await using var driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));
        driver.Sessions =
        [
            new SessionSnapshot(
                SessionId.Parse("rec-1"),
                SessionPurpose.Recording,
                "fake-terrestrial",
                SessionState.Active,
                DateTimeOffset.UtcNow),
            new SessionSnapshot(
                SessionId.Parse("rec-2"),
                SessionPurpose.Recording,
                "fake-satellite",
                SessionState.Stopping,
                DateTimeOffset.UtcNow),
        ];
        using var client = ClientFor(socketPath);

        var call = await client.GetActiveSessionsAsync(CancellationToken.None);

        Assert.True(call.TryGetValue(out var sessions));
        Assert.Equal(2, sessions.Count);
        Assert.Equal("rec-1", sessions[0].SessionId.Value);
        Assert.Equal(SessionState.Stopping, sessions[1].State);
    }

    [Fact]
    public async Task ReadsTheDiagnostics()
    {
        var socketPath = NewSocketPath();
        await using var driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));
        driver.Diagnostics =
        [
            new DiagnosticSnapshot(
                DiagnosticReason.RecordingWriteFailed,
                DateTimeOffset.UtcNow,
                "fake-terrestrial",
                SessionId.Parse("rec-1"),
                "No space left on device"),
        ];
        using var client = ClientFor(socketPath);

        var call = await client.GetDiagnosticsAsync(CancellationToken.None);

        Assert.True(call.TryGetValue(out var diagnostics));
        var entry = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticReason.RecordingWriteFailed, entry.Reason);
        Assert.Equal("rec-1", entry.SessionId.Value);
    }

    [Fact]
    public async Task StartsASessionAndReadsTheCreatedSnapshot()
    {
        var socketPath = NewSocketPath();
        await using var driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));
        using var client = ClientFor(socketPath);

        var call = await client.StartSessionAsync(
            new StartSessionRequest
            {
                SessionId = SessionId.Parse("rec-1"),
                Purpose = SessionPurpose.Recording,
                Tuning = new TuningRequest(TunerKind.Terrestrial, 27),
                OutputRoot = "primary",
                EndsAt = DateTimeOffset.UtcNow.AddHours(1),
            },
            CancellationToken.None);

        Assert.True(call.TryGetValue(out var snapshot));
        Assert.Equal(201, call.StatusCode);
        Assert.Equal("rec-1", snapshot.SessionId.Value);
    }

    [Fact]
    public async Task AStopAcknowledgedWithoutABodyStillReachesTheDriver()
    {
        var socketPath = NewSocketPath();
        await using var driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));
        using var client = ClientFor(socketPath);

        var call = await client.StopSessionAsync(
            SessionId.Parse("rec-1"),
            CancellationToken.None);

        Assert.Equal(DriverCallOutcome.Reached, call.Outcome);
        Assert.Equal(202, call.StatusCode);
        Assert.False(call.TryGetValue(out _));
    }

    [Fact]
    public async Task AnAbortedSessionStreamSurfacesAsAFailedReadNotACleanEnd()
    {
        var socketPath = NewSocketPath();
        await using var driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));
        using var client = ClientFor(socketPath);

        var call = await client.OpenSessionStreamAsync(
            SessionId.Parse("rec-1"),
            DriverEndpoints.ViewerSubscriber,
            CancellationToken.None);

        Assert.True(call.TryGetValue(out var stream));
        await using (stream)
        {
            var received = 0;
            var buffer = new byte[188];

            while (received < buffer.Length)
            {
                var read = await stream.ReadAsync(
                    buffer.AsMemory(received),
                    CancellationToken.None);
                Assert.NotEqual(0, read);
                received += read;
            }

            driver.StreamAbortGate.Release();

            var error = await Record.ExceptionAsync(async () =>
            {
                using var sink = new MemoryStream();
                await stream.CopyToAsync(sink, CancellationToken.None);
            });

            Assert.True(
                error is IOException or HttpRequestException,
                $"Expected a broken read, got: {error?.GetType().Name ?? "a clean end"}");
        }
    }

    [Fact]
    public async Task ReadsSignalNamesFromTheEventStream()
    {
        var socketPath = NewSocketPath();
        await using var driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));
        using var client = ClientFor(socketPath);

        var call = await client.OpenEventsAsync(CancellationToken.None);

        Assert.True(call.TryGetValue(out var stream));
        await using (stream)
        {
            driver.Signal("tuners");
            driver.Signal("sessions");

            var names = new List<string>();

            await foreach (var name in SseFrames.ReadNamesAsync(
                stream,
                CancellationToken.None))
            {
                names.Add(name);

                if (names.Count == 2)
                {
                    break;
                }
            }

            Assert.Equal(["tuners", "sessions"], names);
        }
    }

    [Fact]
    public async Task ARefusedEventSubscriptionSurfacesTheProblem()
    {
        var socketPath = NewSocketPath();
        await using var driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));
        driver.RefuseEverythingWith = new DriverProblem("draining", ["No further events."]);
        using var client = ClientFor(socketPath);

        var call = await client.OpenEventsAsync(CancellationToken.None);

        Assert.Equal(DriverCallOutcome.Refused, call.Outcome);
        Assert.Equal("draining", call.Problem?.Title);
    }
}
