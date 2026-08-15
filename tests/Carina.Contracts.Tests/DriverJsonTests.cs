using System.Text.Json;

namespace Carina.Contracts.Tests;

public sealed class DriverJsonTests
{
    private static readonly DateTimeOffset Moment =
        new(2026, 8, 8, 21, 4, 0, TimeSpan.FromHours(9));

    [Fact]
    public void HelloSerialisesToItsAgreedForm()
    {
        var json = DriverJson.Serialize(
            new DriverHello(1, "b7f2c9", [DriverCapabilities.Recording, DriverCapabilities.Live])
        );

        Assert.Equal(
            """{"protocolVersion":1,"instanceId":"b7f2c9","capabilities":["recording","live"],"draining":false}""",
            json
        );
    }

    [Fact]
    public void StartSessionRequestSerialisesToItsAgreedForm()
    {
        var json = DriverJson.Serialize(
            new StartSessionRequest
            {
                SessionId = SessionId.Parse("rec-1"),
                Purpose = SessionPurpose.Recording,
                Tuning = new TuningRequest(TunerKind.Terrestrial, 55, 50001),
                DeviceId = "adapter0",
                OutputRoot = "primary",
                EndsAt = Moment,
            }
        );

        Assert.Equal(
            """{"sessionId":"rec-1","purpose":"recording","tuning":{"kind":"terrestrial","physicalChannel":55,"serviceId":50001},"deviceId":"adapter0","outputRoot":"primary","endsAt":"2026-08-08T21:04:00+09:00","tune":null}""",
            json
        );
    }

    private const string LiveSessionForm =
        """{"sessionId":"s-1","purpose":"live","state":"active","startedAt":"2026-08-08T21:04:00+09:00","endsAt":null,"deviceId":"adapter1","stopReason":"unspecified","concluded":false,"instanceId":null,"outputRoot":null,"bytesRecorded":0,"faultCount":0,"droppedChunks":0,"firstFault":null,"failureCause":null,"counters":{"packets":0,"drops":0,"duplicates":0,"discontinuities":0,"transportErrors":0,"scrambledPackets":0,"provisionalPackets":0,"discardedBytes":0,"resyncs":0,"deviceOverflows":0,"lockLosses":0}}""";

    private static SessionSnapshot LiveSession =>
        new(SessionId.Parse("s-1"), SessionPurpose.Live, "adapter1", SessionState.Active, Moment);

    [Fact]
    public void SessionSnapshotSerialisesToItsAgreedForm()
    {
        Assert.Equal(LiveSessionForm, DriverJson.Serialize(LiveSession));
    }

    [Fact]
    public void ASessionCarriesWhatItsQualityLookedLike()
    {
        var json = DriverJson.Serialize(
            LiveSession with
            {
                FaultCount = 3,
                FirstFault = "the subscriber did not take the stream",
                Counters = new SessionCounters(Packets: 1000, Drops: 7, Resyncs: 2),
            }
        );

        Assert.Contains("\"faultCount\":3", json);
        Assert.Contains("\"firstFault\":\"the subscriber did not take the stream\"", json);
        Assert.Contains("\"drops\":7", json);
        Assert.Contains("\"resyncs\":2", json);
    }

    [Fact]
    public void ASessionSaysWhetherTheDriverSawItFinish()
    {
        var json = DriverJson.Serialize(
            LiveSession with
            {
                State = SessionState.Stopping,
                StopReason = SessionStopReason.DrainCapReached,
                Concluded = false,
                InstanceId = "b7f2c9",
            }
        );

        Assert.Contains("\"stopReason\":\"drainCapReached\"", json);
        Assert.Contains("\"concluded\":false", json);
        Assert.Contains("\"instanceId\":\"b7f2c9\"", json);
    }

    [Fact]
    public void TunerSnapshotSerialisesToItsAgreedForm()
    {
        var json = DriverJson.Serialize(
            new TunerSnapshot(
                "adapter2",
                TunerKind.Satellite,
                TunerState.Faulted,
                Detail: "kind mismatch"
            )
        );

        Assert.Equal(
            """{"kind":"satellite","state":"faulted","sessionId":null,"detail":"kind mismatch","deviceId":"adapter2","health":null,"signalQuality":null,"currentSession":null,"toggled":false}""",
            json
        );
    }

    [Fact]
    public void DiagnosticSnapshotSerialisesToItsAgreedForm()
    {
        var json = DriverJson.Serialize(
            new DiagnosticSnapshot(
                DiagnosticReason.DiskSpaceLow,
                Moment,
                "adapter0",
                SessionId.Parse("s-1"),
                "3% left on the output volume"
            )
        );

        Assert.Equal(
            """{"reason":"diskSpaceLow","occurredAt":"2026-08-08T21:04:00+09:00","deviceId":"adapter0","sessionId":"s-1","detail":"3% left on the output volume"}""",
            json
        );
    }

    [Fact]
    public void SessionListIsABareArray()
    {
        Assert.Equal(
            $"[{LiveSessionForm}]",
            DriverJson.Serialize<IReadOnlyList<SessionSnapshot>>([LiveSession])
        );

        Assert.Equal("[]", DriverJson.Serialize<IReadOnlyList<SessionSnapshot>>([]));
    }

    [Fact]
    public void TunerListIsABareArray()
    {
        Assert.Equal(
            """[{"kind":"terrestrial","state":"idle","sessionId":null,"detail":null,"deviceId":"adapter0","health":null,"signalQuality":null,"currentSession":null,"toggled":false}]""",
            DriverJson.Serialize<IReadOnlyList<TunerSnapshot>>(
                [new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Idle)]
            )
        );

        Assert.Equal("[]", DriverJson.Serialize<IReadOnlyList<TunerSnapshot>>([]));
    }

    [Fact]
    public void ATerrestrialTuneSerialisesToItsAgreedForm()
    {
        Assert.Equal(
            """{"system":"isdbT","isdbT":{"physicalChannel":55},"isdbSBs":null,"isdbSCs110":null}""",
            DriverJson.Serialize(TuneParams.Terrestrial(55))
        );
    }

    [Fact]
    public void ABsTuneSerialisesToItsAgreedForm()
    {
        Assert.Equal(
            """{"system":"isdbSBs","isdbT":null,"isdbSBs":{"bsChannel":15,"tsid":50001},"isdbSCs110":null}""",
            DriverJson.Serialize(TuneParams.Bs(15, 50001))
        );
    }

    [Fact]
    public void ACs110TuneSerialisesToItsAgreedForm()
    {
        Assert.Equal(
            """{"system":"isdbSCs110","isdbT":null,"isdbSBs":null,"isdbSCs110":{"csChannel":24}}""",
            DriverJson.Serialize(TuneParams.Cs110(24))
        );
    }

    [Fact]
    public void ATypedSessionRequestSerialisesToItsAgreedForm()
    {
        var json = DriverJson.Serialize(
            new StartSessionRequest
            {
                SessionId = SessionId.Parse("scan-1"),
                Purpose = SessionPurpose.Scan,
                Tuning = TuneParams.Terrestrial(55).ToLegacyRequest(),
                Tune = TuneParams.Terrestrial(55),
            }
        );

        Assert.Equal(
            """{"sessionId":"scan-1","purpose":"scan","tuning":{"kind":"terrestrial","physicalChannel":55,"serviceId":null},"deviceId":null,"outputRoot":null,"endsAt":null,"tune":{"system":"isdbT","isdbT":{"physicalChannel":55},"isdbSBs":null,"isdbSCs110":null}}""",
            json
        );
    }

    [Fact]
    public void ALockedQualityReadingSerialisesToItsAgreedForm()
    {
        var json = DriverJson.Serialize(
            new SignalQualityDto
            {
                Lock = SignalLock.Locked,
                CnrMilliDecibels = 21_500,
                PostViterbiBitErrors =
                [
                    new LayerBitErrorCounts(0, 12, 1_000_000),
                    new LayerBitErrorCounts(1, 0, 500_000),
                ],
                MeasuredAt = Moment,
            }
        );

        Assert.Equal(
            """{"lock":"locked","cnrMilliDecibels":21500,"postViterbiBitErrors":[{"layer":0,"errorBits":12,"totalBits":1000000},{"layer":1,"errorBits":0,"totalBits":500000}],"measuredAt":"2026-08-08T21:04:00+09:00","lockReadAt":null,"notImplementedMetrics":[]}""",
            json
        );
    }

    [Fact]
    public void AnUnlockedQualityReadingSerialisesToItsAgreedForm()
    {
        Assert.Equal(
            """{"lock":"notLocked","cnrMilliDecibels":null,"postViterbiBitErrors":[],"measuredAt":"2026-08-08T21:04:00+09:00","lockReadAt":null,"notImplementedMetrics":[]}""",
            DriverJson.Serialize(SignalQualityDto.NotLocked(Moment))
        );
    }

    [Fact]
    public void ATunerCarryingEverythingSerialisesToItsAgreedForm()
    {
        var json = DriverJson.Serialize(
            new TunerSnapshot("adapter0", TunerKind.Satellite, TunerState.Busy)
            {
                SessionId = SessionId.Parse("scan-1"),
                Health = new TunerHealthDto
                {
                    Level = TunerHealthLevel.Healthy,
                    DisablePending = true,
                    LnbPowered = true,
                    ChangedAt = Moment,
                },
                SignalQuality = SignalQualityDto.NotLocked(Moment),
                CurrentSession = new CurrentSessionDto
                {
                    SessionId = SessionId.Parse("scan-1"),
                    Purpose = SessionPurpose.Scan,
                    StartedAt = Moment,
                    Tune = TuneParams.Cs110(24),
                },
            }
        );

        Assert.Equal(
            """{"kind":"satellite","state":"busy","sessionId":"scan-1","detail":null,"deviceId":"adapter0","health":{"level":"healthy","disablePending":true,"lnbPowered":true,"detail":null,"changedAt":"2026-08-08T21:04:00+09:00"},"signalQuality":{"lock":"notLocked","cnrMilliDecibels":null,"postViterbiBitErrors":[],"measuredAt":"2026-08-08T21:04:00+09:00","lockReadAt":null,"notImplementedMetrics":[]},"currentSession":{"sessionId":"scan-1","purpose":"scan","startedAt":"2026-08-08T21:04:00+09:00","tune":{"system":"isdbSCs110","isdbT":null,"isdbSBs":null,"isdbSCs110":{"csChannel":24}},"endsAt":null},"toggled":false}""",
            json
        );
    }

    [Fact]
    public void ADetectedDeviceListIsABareArray()
    {
        Assert.Equal(
            """[{"deviceId":"adapter0","detection":"detected","kinds":["terrestrial"],"detail":null}]""",
            DriverJson.Serialize<IReadOnlyList<DetectedDeviceDto>>(
                [
                    new DetectedDeviceDto
                    {
                        DeviceId = "adapter0",
                        Detection = DeviceDetection.Detected,
                        Kinds = [TunerKind.Terrestrial],
                    },
                ]
            )
        );
    }

    [Theory]
    [InlineData("2026-08-08T21:04:00.1234567+09:00")]
    [InlineData("2026-08-08T12:04:00+00:00")]
    public void TimestampsKeepWhateverPrecisionAndOffsetTheDriverReports(string wire)
    {
        var json =
            $$"""{"sessionId":"s-1","purpose":"live","deviceId":"a0","state":"active","startedAt":"{{wire}}","endsAt":null}""";

        var restored = DriverJson.Deserialize(json, DriverJson.Context.SessionSnapshot);

        Assert.NotNull(restored);
        Assert.Equal(DateTimeOffset.Parse(wire), restored.StartedAt);
    }

    [Fact]
    public void UnknownFieldsAreIgnored()
    {
        var hello = DriverJson.Deserialize(
            """{"protocolVersion":1,"instanceId":"b7f2c9","capabilities":["recording"],"somethingNew":{"a":1}}""",
            DriverJson.Context.DriverHello
        );

        Assert.NotNull(hello);
        Assert.Equal(1, hello.ProtocolVersion);
        Assert.True(hello.Supports(DriverCapabilities.Recording));
    }

    [Fact]
    public void RoundTripKeepsTheValues()
    {
        var request = new StartSessionRequest
        {
            SessionId = SessionId.Parse("s-1"),
            Purpose = SessionPurpose.Survey,
            Tuning = new TuningRequest(TunerKind.Satellite, 15),
        };

        var restored = DriverJson.Deserialize(
            DriverJson.Serialize(request),
            DriverJson.Context.StartSessionRequest
        );

        Assert.Equal(request, restored);
    }

    [Fact]
    public void ARequestMissingItsMembersIsRejected()
    {
        Assert.Throws<JsonException>(
            () => DriverJson.Deserialize("{}", DriverJson.Context.StartSessionRequest)
        );
    }

    [Fact]
    public void ARequestThatNamesNoSessionIsRejected()
    {
        Assert.Throws<JsonException>(
            () =>
                DriverJson.Deserialize(
                    """{"purpose":"live","tuning":{"kind":"terrestrial","physicalChannel":55}}""",
                    DriverJson.Context.StartSessionRequest
                )
        );
    }

    [Fact]
    public void ARequestMayNotSmuggleAPathThroughTheOutputRoot()
    {
        var request = DriverJson.Deserialize(
            """{"sessionId":"rec-1","purpose":"recording","tuning":{"kind":"terrestrial","physicalChannel":55},"outputRoot":"/etc","endsAt":"2026-08-08T22:04:00+09:00"}""",
            DriverJson.Context.StartSessionRequest
        );

        Assert.NotNull(request);
        Assert.Contains(
            request.Validate(Moment),
            problem => problem.StartsWith("outputRoot:")
        );
    }
}
