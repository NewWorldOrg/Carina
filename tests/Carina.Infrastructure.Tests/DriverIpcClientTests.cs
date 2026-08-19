using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Infrastructure.Configuration;
using Carina.Infrastructure.Driver;
using Carina.TestSupport;

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

    private static string[] TunerKeepingCapabilities =>
    [
        DriverCapabilities.Recording,
        DriverCapabilities.Live,
        DriverCapabilities.DeviceDetection,
        DriverCapabilities.TunerLedger,
        DriverCapabilities.LiveTunerToggle,
        DriverCapabilities.TypedTuning,
    ];

    [Fact]
    public async Task ReadsTheDriversHello()
    {
        string socketPath = NewSocketPath();
        await using FakeDriver driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));
        using DriverIpcClient client = ClientFor(socketPath);

        DriverCall<DriverHello> call = await client.GetHealthAsync(CancellationToken.None);

        Assert.True(call.TryGetValue(out DriverHello? hello));
        Assert.Equal(DriverProtocol.Version, hello.ProtocolVersion);
        Assert.Equal("instance-a", hello.InstanceId);
        Assert.Equal(["recording", "live"], hello.Capabilities);
    }

    [Fact]
    public async Task ARefusalSurfacesTheProblemInsteadOfThrowing()
    {
        string socketPath = NewSocketPath();
        await using FakeDriver driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));
        driver.RefuseEverythingWith = new DriverProblem("draining", ["The driver is shutting down."]);
        using DriverIpcClient client = ClientFor(socketPath);

        DriverCall<DriverHello> call = await client.GetHealthAsync(CancellationToken.None);

        Assert.Equal(DriverCallOutcome.Refused, call.Outcome);
        Assert.Equal("draining", call.Problem?.Title);
    }

    [Fact]
    public async Task AMissingSocketIsUnreachableNotAnException()
    {
        using DriverIpcClient client = ClientFor(NewSocketPath());

        DriverCall<DriverHello> call = await client.GetHealthAsync(CancellationToken.None);

        Assert.Equal(DriverCallOutcome.Unreachable, call.Outcome);
        Assert.NotNull(call.Failure);
    }

    [Fact]
    public async Task AStaleSocketFileIsUnreachableNotAnException()
    {
        string socketPath = NewSocketPath();
        await File.WriteAllTextAsync(socketPath, string.Empty, CancellationToken.None);
        using DriverIpcClient client = ClientFor(socketPath);

        DriverCall<DriverHello> call = await client.GetHealthAsync(CancellationToken.None);

        Assert.Equal(DriverCallOutcome.Unreachable, call.Outcome);
    }

    [Fact]
    public async Task ATruncatedBodyReadsAsUnreachableNotAsAnAnswer()
    {
        string socketPath = NewSocketPath();
        await using FakeDriver driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));
        driver.TruncateHealth = true;
        using DriverIpcClient client = ClientFor(socketPath);

        DriverCall<DriverHello> call = await client.GetHealthAsync(CancellationToken.None);

        Assert.Equal(DriverCallOutcome.Unreachable, call.Outcome);
    }

    [Fact]
    public async Task ReadsTheActiveSessions()
    {
        string socketPath = NewSocketPath();
        await using FakeDriver driver = await FakeDriver.StartAsync(
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
        using DriverIpcClient client = ClientFor(socketPath);

        DriverCall<IReadOnlyList<SessionSnapshot>> call = await client.GetActiveSessionsAsync(CancellationToken.None);

        Assert.True(call.TryGetValue(out IReadOnlyList<SessionSnapshot>? sessions));
        Assert.Equal(2, sessions.Count);
        Assert.Equal("rec-1", sessions[0].SessionId.Value);
        Assert.Equal(SessionState.Stopping, sessions[1].State);
    }

    [Fact]
    public async Task ReadsTheDiagnostics()
    {
        string socketPath = NewSocketPath();
        await using FakeDriver driver = await FakeDriver.StartAsync(
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
        using DriverIpcClient client = ClientFor(socketPath);

        DriverCall<IReadOnlyList<DiagnosticSnapshot>> call = await client.GetDiagnosticsAsync(CancellationToken.None);

        Assert.True(call.TryGetValue(out IReadOnlyList<DiagnosticSnapshot>? diagnostics));
        DiagnosticSnapshot entry = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticReason.RecordingWriteFailed, entry.Reason);
        Assert.Equal("rec-1", entry.SessionId.Value);
    }

    [Fact]
    public async Task ReadsTheDetectedDevices()
    {
        string socketPath = NewSocketPath();
        await using FakeDriver driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a", capabilities: TunerKeepingCapabilities));
        driver.DetectedDevices =
        [
            new DetectedDeviceDto
            {
                DeviceId = "adapter0",
                Detection = DeviceDetection.Detected,
                Kinds = [TunerKind.Terrestrial],
            },
            new DetectedDeviceDto
            {
                DeviceId = "adapter1",
                Detection = DeviceDetection.Busy,
                Detail = "held by another process",
            },
        ];
        using DriverIpcClient client = ClientFor(socketPath);

        DriverCall<IReadOnlyList<DetectedDeviceDto>> call = await client.GetDetectedDevicesAsync(CancellationToken.None);

        Assert.True(call.TryGetValue(out IReadOnlyList<DetectedDeviceDto>? devices));
        Assert.Equal(2, devices.Count);
        Assert.Equal("adapter0", devices[0].DeviceId);
        Assert.Equal([TunerKind.Terrestrial], devices[0].Kinds);
        Assert.Equal(DeviceDetection.Busy, devices[1].Detection);
    }

    [Fact]
    public async Task ReadsTheLedgerWithItsDriftHashes()
    {
        string socketPath = NewSocketPath();
        await using FakeDriver driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a", capabilities: TunerKeepingCapabilities));
        driver.Ledger = new TunerLedgerDto
        {
            Tuners = [new TunerConfigEntry { DeviceId = "adapter0", LnbPower = true }],
            LoadedHash = "aaaa",
            SavedHash = "bbbb",
        };
        using DriverIpcClient client = ClientFor(socketPath);

        DriverCall<TunerLedgerDto> call = await client.GetTunerLedgerAsync(CancellationToken.None);

        Assert.True(call.TryGetValue(out TunerLedgerDto? ledger));
        Assert.Equal("adapter0", Assert.Single(ledger.Tuners).DeviceId);
        Assert.True(ledger.HasDrifted());
    }

    [Fact]
    public async Task ReplacesTheLedgerAndReadsTheAnswerBack()
    {
        string socketPath = NewSocketPath();
        await using FakeDriver driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a", capabilities: TunerKeepingCapabilities));
        driver.Ledger = new TunerLedgerDto
        {
            Tuners = [new TunerConfigEntry { DeviceId = "adapter0", Disabled = true }],
            LoadedHash = "cccc",
            SavedHash = "cccc",
        };
        using DriverIpcClient client = ClientFor(socketPath);

        DriverCall<TunerLedgerDto> call = await client.ReplaceTunerLedgerAsync(
            [new TunerConfigEntry { DeviceId = "adapter0", Disabled = true }],
            CancellationToken.None);

        Assert.True(call.TryGetValue(out TunerLedgerDto? ledger));
        Assert.False(ledger.HasDrifted());
        Assert.Equal("adapter0", Assert.Single(driver.LastReplacedLedger!).DeviceId);
        Assert.True(driver.LastReplacedLedger![0].Disabled);
    }

    [Fact]
    public async Task AnEmptyLedgerReplacementSurfacesTheDriversRefusal()
    {
        string socketPath = NewSocketPath();
        await using FakeDriver driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a", capabilities: TunerKeepingCapabilities));
        using DriverIpcClient client = ClientFor(socketPath);

        DriverCall<TunerLedgerDto> call = await client.ReplaceTunerLedgerAsync([], CancellationToken.None);

        Assert.Equal(DriverCallOutcome.Refused, call.Outcome);
        Assert.Equal("emptyLedger", call.Problem?.Title);
    }

    [Fact]
    public async Task ALedgerNamingAnUnknownDeviceSurfacesTheDriversRefusal()
    {
        string socketPath = NewSocketPath();
        await using FakeDriver driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a", capabilities: TunerKeepingCapabilities));
        driver.RefusalsByPath[DriverEndpoints.Tuners] = new FakeDriver.Refusal(
            400,
            new DriverProblem("unknownDevice", ["This driver detected no device called 'adapter9'."]));
        using DriverIpcClient client = ClientFor(socketPath);

        DriverCall<TunerLedgerDto> call = await client.ReplaceTunerLedgerAsync(
            [new TunerConfigEntry { DeviceId = "adapter9" }],
            CancellationToken.None);

        Assert.Equal(DriverCallOutcome.Refused, call.Outcome);
        Assert.Equal("unknownDevice", call.Problem?.Title);
    }

    [Fact]
    public async Task TogglesATunerAndReadsTheAnsweredSnapshot()
    {
        string socketPath = NewSocketPath();
        await using FakeDriver driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a", capabilities: TunerKeepingCapabilities));
        driver.Tuners =
        [
            new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Disabled),
        ];
        using DriverIpcClient client = ClientFor(socketPath);

        DriverCall<TunerSnapshot> call = await client.ToggleTunerAsync("adapter0", disabled: true, CancellationToken.None);

        Assert.True(call.TryGetValue(out TunerSnapshot? tuner));
        Assert.Equal("adapter0", tuner.DeviceId);
        Assert.True(tuner.Toggled);
        Assert.Equal("adapter0", driver.LastToggledDeviceId);
        Assert.True(driver.LastToggle?.Disabled);
    }

    [Fact]
    public async Task AToggleForATunerTheDriverDoesNotHoldSurfacesTheProblem()
    {
        string socketPath = NewSocketPath();
        await using FakeDriver driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a", capabilities: TunerKeepingCapabilities));
        using DriverIpcClient client = ClientFor(socketPath);

        DriverCall<TunerSnapshot> call = await client.ToggleTunerAsync("adapter9", disabled: false, CancellationToken.None);

        Assert.Equal(DriverCallOutcome.Refused, call.Outcome);
        Assert.Equal("noSuchTuner", call.Problem?.Title);
    }

    [Fact]
    public async Task ADeviceIdOutsideTheShapeIsRefusedWithoutReachingTheDriver()
    {
        string socketPath = NewSocketPath();
        await using FakeDriver driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a", capabilities: TunerKeepingCapabilities));
        using DriverIpcClient client = ClientFor(socketPath);

        DriverCall<TunerSnapshot> call = await client.ToggleTunerAsync("../etc/shadow", disabled: true, CancellationToken.None);

        Assert.Equal(DriverCallOutcome.Refused, call.Outcome);
        Assert.Equal("badDeviceId", call.Problem?.Title);
        Assert.Equal(0, driver.RequestsFor(DriverEndpoints.Health));
    }

    [Theory]
    [InlineData("detected")]
    [InlineData("ledgerRead")]
    [InlineData("ledgerReplace")]
    [InlineData("toggle")]
    public async Task ACallTheDriverDoesNotDeclareIsRefusedLocallyInsteadOfAsARawNotFound(string surface)
    {
        string socketPath = NewSocketPath();
        await using FakeDriver driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-old"));
        using DriverIpcClient client = ClientFor(socketPath);

        (DriverCallOutcome Outcome, DriverProblem? Problem) outcome = surface switch
        {
            "detected" => Of(await client.GetDetectedDevicesAsync(CancellationToken.None)),
            "ledgerRead" => Of(await client.GetTunerLedgerAsync(CancellationToken.None)),
            "ledgerReplace" => Of(await client.ReplaceTunerLedgerAsync(
                [new TunerConfigEntry { DeviceId = "adapter0" }],
                CancellationToken.None)),
            _ => Of(await client.ToggleTunerAsync("adapter0", disabled: true, CancellationToken.None)),
        };

        Assert.Equal(DriverCallOutcome.Refused, outcome.Outcome);
        Assert.Equal("capabilityMissing", outcome.Problem?.Title);
        Assert.NotEmpty(outcome.Problem!.Problems);
        Assert.Equal(0, driver.RequestsFor(DriverEndpoints.DevicesDetected));
        Assert.Equal(0, driver.RequestsFor(DriverEndpoints.TunerLedger));
        Assert.Equal(0, driver.RequestsFor(DriverEndpoints.Tuners));
        Assert.Equal(0, driver.RequestsFor(DriverEndpoints.Tuner("adapter0")));
    }

    [Fact]
    public async Task AMissingSocketMakesTheNewCallsUnreachableNotAnException()
    {
        using DriverIpcClient client = ClientFor(NewSocketPath());

        DriverCall<TunerLedgerDto> call = await client.GetTunerLedgerAsync(CancellationToken.None);

        Assert.Equal(DriverCallOutcome.Unreachable, call.Outcome);
        Assert.NotNull(call.Failure);
    }

    [Fact]
    public async Task ATypedTuneTravelsToTheDriverBesideTheOlderFields()
    {
        string socketPath = NewSocketPath();
        await using FakeDriver driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a", capabilities: TunerKeepingCapabilities));
        using DriverIpcClient client = ClientFor(socketPath);

        var tune = TuneParams.Bs(15, 50001);

        DriverCall<SessionSnapshot> call = await client.StartSessionAsync(
            new StartSessionRequest
            {
                SessionId = SessionId.Parse("scan-1"),
                Purpose = SessionPurpose.Scan,
                Tuning = tune.ToLegacyRequest(),
                Tune = tune,
            },
            CancellationToken.None);

        Assert.True(call.TryGetValue(out SessionSnapshot? snapshot));
        Assert.Equal("scan-1", snapshot.SessionId.Value);
        Assert.Equal(TuneSystem.IsdbSBs, driver.LastStartRequest?.Tune?.System);
        Assert.Equal(15, driver.LastStartRequest?.Tune?.IsdbSBs?.BsChannel);
        Assert.Equal(50001, driver.LastStartRequest?.Tune?.IsdbSBs?.Tsid);
        Assert.Equal(15, driver.LastStartRequest?.Tuning.PhysicalChannel);
    }

    private static (DriverCallOutcome Outcome, DriverProblem? Problem) Of<T>(DriverCall<T> call)
        => (call.Outcome, call.Problem);

    [Fact]
    public async Task StartsASessionAndReadsTheCreatedSnapshot()
    {
        string socketPath = NewSocketPath();
        await using FakeDriver driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));
        using DriverIpcClient client = ClientFor(socketPath);

        DriverCall<SessionSnapshot> call = await client.StartSessionAsync(
            new StartSessionRequest
            {
                SessionId = SessionId.Parse("rec-1"),
                Purpose = SessionPurpose.Recording,
                Tuning = new TuningRequest(TunerKind.Terrestrial, 55),
                OutputRoot = "primary",
                EndsAt = DateTimeOffset.UtcNow.AddHours(1),
            },
            CancellationToken.None);

        Assert.True(call.TryGetValue(out SessionSnapshot? snapshot));
        Assert.Equal("rec-1", snapshot.SessionId.Value);
    }

    [Fact]
    public async Task AHurriedSurveyIsAskedOfADriverThatPredatesItAsAnOrdinaryOne()
    {
        string socketPath = NewSocketPath();
        await using FakeDriver driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));
        using DriverIpcClient client = ClientFor(socketPath);

        DriverCall<SessionSnapshot> call = await client.StartSessionAsync(
            SurveyFor(SessionPurpose.SurveyNow),
            CancellationToken.None);

        Assert.True(call.TryGetValue(out SessionSnapshot? snapshot));
        Assert.Equal(SessionPurpose.Survey, driver.LastStartRequest?.Purpose);
        Assert.Equal(SessionPurpose.Survey, snapshot.Purpose);
    }

    [Fact]
    public async Task AHurriedSurveyIsAskedForPlainlyOfADriverThatDeclaresIt()
    {
        string socketPath = NewSocketPath();
        await using FakeDriver driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor(
                "instance-a",
                capabilities: [.. TunerKeepingCapabilities, .. SessionPurposes.Capabilities]));
        using DriverIpcClient client = ClientFor(socketPath);

        await client.StartSessionAsync(SurveyFor(SessionPurpose.SurveyNow), CancellationToken.None);

        Assert.Equal(SessionPurpose.SurveyNow, driver.LastStartRequest?.Purpose);
    }

    [Fact]
    public async Task APurposeWithNothingOlderToFallBackOnNeverReachesTheDriver()
    {
        string socketPath = NewSocketPath();
        await using FakeDriver driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));
        using DriverIpcClient client = ClientFor(socketPath);

        DriverCall<SessionSnapshot> call = await client.StartSessionAsync(
            SurveyFor((SessionPurpose)99),
            CancellationToken.None);

        Assert.Equal(DriverCallOutcome.Refused, call.Outcome);
        Assert.Equal(SessionRefusalTitles.CapabilityMissing, call.Problem?.Title);
        Assert.Null(driver.LastStartRequest);
    }

    [Fact]
    public async Task ARecordingIsSentWithoutFirstAskingTheDriverWhatItAccepts()
    {
        string socketPath = NewSocketPath();
        await using FakeDriver driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));
        using DriverIpcClient client = ClientFor(socketPath);

        await client.StartSessionAsync(SurveyFor(SessionPurpose.Recording), CancellationToken.None);

        Assert.Equal(0, driver.RequestsFor(DriverEndpoints.Health));
        Assert.Equal(SessionPurpose.Recording, driver.LastStartRequest?.Purpose);
    }

    private static StartSessionRequest SurveyFor(SessionPurpose purpose)
        => new()
        {
            SessionId = SessionId.Parse("epg-1"),
            Purpose = purpose,
            Tuning = new TuningRequest(TunerKind.Terrestrial, 55),
        };

    [Fact]
    public async Task AStopAcknowledgedWithoutABodyStillReachesTheDriver()
    {
        string socketPath = NewSocketPath();
        await using FakeDriver driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));
        using DriverIpcClient client = ClientFor(socketPath);

        DriverCall<SessionSnapshot> call = await client.StopSessionAsync(
            SessionId.Parse("rec-1"),
            "walk over & done",
            CancellationToken.None);

        Assert.Equal(DriverCallOutcome.Reached, call.Outcome);
        Assert.False(call.TryGetValue(out _));
        Assert.Equal("walk over & done", driver.LastStopReason);
    }

    [Fact]
    public async Task AnAbortedSessionStreamSurfacesAsAFailedReadNotACleanEnd()
    {
        string socketPath = NewSocketPath();
        await using FakeDriver driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));
        using DriverIpcClient client = ClientFor(socketPath);

        DriverCall<Stream> call = await client.OpenSessionStreamAsync(
            SessionId.Parse("rec-1"),
            DriverEndpoints.ViewerSubscriber,
            CancellationToken.None);

        Assert.True(call.TryGetValue(out Stream? stream));
        await using (stream)
        {
            int received = 0;
            byte[] buffer = new byte[188];

            while (received < buffer.Length)
            {
                int read = await stream.ReadAsync(
                    buffer.AsMemory(received),
                    CancellationToken.None);
                Assert.NotEqual(0, read);
                received += read;
            }

            driver.StreamAbortGate.Release();

            Exception error = await Record.ExceptionAsync(async () =>
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
        string socketPath = NewSocketPath();
        await using FakeDriver driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));
        using DriverIpcClient client = ClientFor(socketPath);

        DriverCall<Stream> call = await client.OpenEventsAsync(CancellationToken.None);

        Assert.True(call.TryGetValue(out Stream? stream));
        await using (stream)
        {
            driver.Signal("tuners");
            driver.Signal("sessions");

            var names = new List<string>();

            await foreach (string name in SseFrames.ReadNamesAsync(
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
        string socketPath = NewSocketPath();
        await using FakeDriver driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));
        driver.RefuseEverythingWith = new DriverProblem("draining", ["No further events."]);
        using DriverIpcClient client = ClientFor(socketPath);

        DriverCall<Stream> call = await client.OpenEventsAsync(CancellationToken.None);

        Assert.Equal(DriverCallOutcome.Refused, call.Outcome);
        Assert.Equal("draining", call.Problem?.Title);
    }
}
