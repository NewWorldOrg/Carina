using System.Text.Json;

namespace Carina.Contracts.Tests;

public sealed class WireStabilityTests
{
    private static readonly DateTimeOffset Moment =
        new(2026, 8, 8, 21, 4, 0, TimeSpan.FromHours(9));

    private static readonly string[] HelloFields =
    [
        "protocolVersion",
        "instanceId",
        "capabilities",
        "draining",
    ];

    private static readonly string[] TuningRequestFields =
    [
        "kind",
        "physicalChannel",
        "serviceId",
    ];

    private static readonly string[] StartSessionRequestFields =
    [
        "sessionId",
        "purpose",
        "tuning",
        "deviceId",
        "outputRoot",
        "endsAt",
    ];

    private static readonly string[] SessionSnapshotFields =
    [
        "sessionId",
        "purpose",
        "state",
        "startedAt",
        "endsAt",
        "deviceId",
        "stopReason",
        "concluded",
        "instanceId",
        "outputRoot",
        "bytesRecorded",
        "faultCount",
        "droppedChunks",
        "firstFault",
        "failureCause",
        "counters",
    ];

    private static readonly string[] SessionCountersFields =
    [
        "packets",
        "drops",
        "duplicates",
        "discontinuities",
        "transportErrors",
        "scrambledPackets",
        "provisionalPackets",
        "discardedBytes",
        "resyncs",
    ];

    private static readonly string[] TunerSnapshotFields =
    [
        "kind",
        "state",
        "sessionId",
        "detail",
        "deviceId",
    ];

    private static readonly string[] DiagnosticSnapshotFields =
    [
        "reason",
        "occurredAt",
        "deviceId",
        "sessionId",
        "detail",
    ];

    private static readonly string[] EndpointsTheFrontendReaches =
    [
        "/health",
        "/tuners",
        "/sessions",
        "/diagnostics",
        "/events",
    ];

    [Fact]
    public void TheHelloKeepsExactlyTheFieldsItHad()
    {
        Assert.Equal(
            HelloFields,
            FieldsOf(DriverJson.Serialize(new DriverHello(1, "b7f2c9", ["recording"])))
        );
    }

    [Fact]
    public void ATuningRequestKeepsExactlyTheFieldsItHad()
    {
        Assert.Equal(
            TuningRequestFields,
            FieldsOf(DriverJson.Serialize(new TuningRequest(TunerKind.Terrestrial, 55, 50001)))
        );
    }

    [Fact]
    public void ASessionRequestKeepsItsFieldsAndTakesTheNewOneAtTheEnd()
    {
        var fields = FieldsOf(DriverJson.Serialize(LegacyRequest));

        Assert.Equal(StartSessionRequestFields, fields.Take(StartSessionRequestFields.Length));
        Assert.Equal(["tune"], fields.Skip(StartSessionRequestFields.Length));
    }

    [Fact]
    public void ASessionSnapshotKeepsExactlyTheFieldsItHad()
    {
        Assert.Equal(SessionSnapshotFields, FieldsOf(DriverJson.Serialize(LiveSession)));
    }

    [Fact]
    public void TheCountersKeepTheirFieldsAndTakeTheNewOnesAtTheEnd()
    {
        var fields = FieldsOf(DriverJson.Serialize(SessionCounters.Nothing));

        Assert.Equal(SessionCountersFields, fields.Take(SessionCountersFields.Length));
        Assert.Equal(
            ["deviceOverflows", "lockLosses"],
            fields.Skip(SessionCountersFields.Length)
        );
    }

    [Fact]
    public void CountersFromADriverThatCountedNeitherOfTheNewOnesStillRead()
    {
        var counters = DriverJson.Deserialize(
            """{"packets":1000,"drops":7,"discardedBytes":188,"resyncs":2}""",
            DriverJson.Context.SessionCounters
        );

        Assert.NotNull(counters);
        Assert.Equal(1000, counters.Packets);
        Assert.Equal(0, counters.DeviceOverflows);
        Assert.Equal(0, counters.LockLosses);
    }

    [Fact]
    public void ATunerKeepsItsFieldsAndTakesTheNewSubtreesAtTheEnd()
    {
        var fields = FieldsOf(
            DriverJson.Serialize(new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Idle))
        );

        Assert.Equal(TunerSnapshotFields, fields.Take(TunerSnapshotFields.Length));
        Assert.Equal(
            ["health", "signalQuality", "currentSession", "toggled"],
            fields.Skip(TunerSnapshotFields.Length)
        );
    }

    [Fact]
    public void ADiagnosticKeepsExactlyTheFieldsItHad()
    {
        Assert.Equal(
            DiagnosticSnapshotFields,
            FieldsOf(
                DriverJson.Serialize(new DiagnosticSnapshot(DiagnosticReason.DiskSpaceLow, Moment))
            )
        );
    }

    [Fact]
    public void TheSubtreesATunerGainedAreExplicitNullsUntilADriverFillsThem()
    {
        var json = DriverJson.Serialize(
            new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Idle)
        );

        Assert.Contains("\"health\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"signalQuality\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"currentSession\":null", json, StringComparison.Ordinal);
    }

    [Fact]
    public void TheValuesOfASessionRequestAreUntouchedByTheNewField()
    {
        Assert.Equal(
            """{"sessionId":"rec-1","purpose":"recording","tuning":{"kind":"terrestrial","physicalChannel":55,"serviceId":50001},"deviceId":"adapter0","outputRoot":"primary","endsAt":"2026-08-08T21:04:00+09:00","tune":null}""",
            DriverJson.Serialize(LegacyRequest)
        );
    }

    [Fact]
    public void ARequestWrittenBeforeAnyOfThisStillValidatesTheSameWay()
    {
        var request = DriverJson.Deserialize(
            """{"sessionId":"rec-1","purpose":"recording","tuning":{"kind":"terrestrial","physicalChannel":55},"outputRoot":"primary","endsAt":"2026-08-08T22:04:00+09:00"}""",
            DriverJson.Context.StartSessionRequest
        );

        Assert.NotNull(request);
        Assert.Null(request.Tune);
        Assert.Empty(request.Validate(Moment));
    }

    [Fact]
    public void ARequestWrittenBeforeAnyOfThisIsRefusedForTheSameReasons()
    {
        var request = DriverJson.Deserialize(
            """{"sessionId":"rec-1","purpose":"recording","tuning":{"kind":"terrestrial","physicalChannel":900}}""",
            DriverJson.Context.StartSessionRequest
        );

        Assert.NotNull(request);
        Assert.Equal(
            [
                "outputRoot: a recording names one of the output roots this driver declares.",
                "tuning.physicalChannel: expected 1 to 255, got 900.",
                "endsAt: a recording session has to carry its own end time.",
            ],
            request.Validate(Moment)
        );
    }

    [Fact]
    public void TheNamesTheFrontendAlreadyAsksForStillAnswer()
    {
        Assert.Equal(EndpointsTheFrontendReaches, DriverEndpoints.All.Take(5));
    }

    [Theory]
    [InlineData(SessionPurpose.Unspecified, 0)]
    [InlineData(SessionPurpose.Recording, 1)]
    [InlineData(SessionPurpose.Live, 2)]
    [InlineData(SessionPurpose.Survey, 3)]
    [InlineData(SessionPurpose.Scan, 4)]
    public void TheAgreedPurposesKeepTheirPlace(SessionPurpose purpose, int value)
    {
        Assert.Equal(value, (int)purpose);
    }

    [Fact]
    public void TheProjectionIntoTheOlderFieldIsWireSurfaceBecauseEachProcessCompilesItsOwnCopy()
    {
        Assert.Equal(
            new TuningRequest(TunerKind.Terrestrial, 55),
            TuneParams.Terrestrial(55).ToLegacyRequest()
        );
        Assert.Equal(
            new TuningRequest(TunerKind.Unspecified, 15),
            TuneParams.Bs(15, 50001).ToLegacyRequest()
        );
        Assert.Equal(
            new TuningRequest(TunerKind.Unspecified, 24),
            TuneParams.Cs110(24).ToLegacyRequest()
        );
    }

    [Fact]
    public void TheTypedTuneKeepsExactlyTheFieldsItWasGiven()
    {
        Assert.Equal(
            ["system", "isdbT", "isdbSBs", "isdbSCs110"],
            FieldsOf(DriverJson.Serialize(TuneParams.Terrestrial(55)))
        );
        Assert.Equal(
            ["physicalChannel"],
            FieldsOf(DriverJson.Serialize(new IsdbTParams(55)))
        );
        Assert.Equal(
            ["bsChannel", "tsid"],
            FieldsOf(DriverJson.Serialize(new IsdbSBsParams(15, 50001)))
        );
        Assert.Equal(
            ["csChannel"],
            FieldsOf(DriverJson.Serialize(new IsdbSCs110Params(24)))
        );
    }

    [Fact]
    public void AQualityReadingKeepsItsFieldsAndTakesTheNewOnesAtTheEnd()
    {
        var fields = FieldsOf(DriverJson.Serialize(SignalQualityDto.NotLocked(Moment)));

        Assert.Equal(
            ["lock", "cnrMilliDecibels", "postViterbiBitErrors", "measuredAt"],
            fields.Take(4)
        );
        Assert.Equal(["lockReadAt", "notImplementedMetrics"], fields.Skip(4));
        Assert.Equal(
            ["layer", "errorBits", "totalBits"],
            FieldsOf(DriverJson.Serialize(new LayerBitErrorCounts(0, 12, 1_000_000)))
        );
    }

    [Fact]
    public void ATunerHealthKeepsExactlyTheFieldsItWasGiven()
    {
        Assert.Equal(
            ["level", "disablePending", "lnbPowered", "detail", "changedAt"],
            FieldsOf(DriverJson.Serialize(new TunerHealthDto()))
        );
    }

    [Fact]
    public void ACurrentSessionKeepsItsFieldsAndTakesTheNewOnesAtTheEnd()
    {
        var fields = FieldsOf(DriverJson.Serialize(new CurrentSessionDto()));

        Assert.Equal(["sessionId", "purpose", "startedAt", "tune"], fields.Take(4));
        Assert.Equal(["endsAt"], fields.Skip(4));
    }

    [Fact]
    public void ALedgerEntryKeepsExactlyTheFieldsItWasGiven()
    {
        Assert.Equal(
            ["deviceId", "disabled", "lnbPower"],
            FieldsOf(DriverJson.Serialize(new TunerConfigEntry { DeviceId = "adapter0" }))
        );
    }

    [Fact]
    public void ADetectedDeviceKeepsExactlyTheFieldsItWasGiven()
    {
        Assert.Equal(
            ["deviceId", "detection", "kinds", "detail"],
            FieldsOf(DriverJson.Serialize(new DetectedDeviceDto { DeviceId = "adapter0" }))
        );
    }

    [Theory]
    [InlineData(TunerState.Idle, 1)]
    [InlineData(TunerState.Busy, 2)]
    [InlineData(TunerState.Disabled, 3)]
    [InlineData(TunerState.Faulted, 4)]
    public void TheAgreedTunerStatesKeepTheirPlace(TunerState state, int value)
    {
        Assert.Equal(value, (int)state);
    }

    [Fact]
    public void TheStateATunerGainedTookTheNextNumberRatherThanOneOfTheAgreedOnes()
    {
        Assert.Equal(5, (int)TunerState.Draining);
    }

    [Fact]
    public void ALedgerAnswerKeepsExactlyTheFieldsItWasGiven()
    {
        Assert.Equal(
            ["tuners", "loadedHash", "savedHash"],
            FieldsOf(DriverJson.Serialize(new TunerLedgerDto()))
        );
    }

    [Fact]
    public void AToggleKeepsExactlyTheFieldsItWasGiven()
    {
        Assert.Equal(
            ["disabled"],
            FieldsOf(DriverJson.Serialize(new TunerToggleRequest { Disabled = true }))
        );
    }

    [Fact]
    public void TheLedgerPathsTakeTheirPlaceAfterTheOnesThatWereAlreadyAnswered()
    {
        Assert.Equal(
            ["/devices/detected", "/tuners/ledger"],
            DriverEndpoints.All.Skip(EndpointsTheFrontendReaches.Length).Take(2)
        );
    }

    [Fact]
    public void AskingTheDriverToStopTakesItsPlaceAfterEverythingThatCameBefore()
    {
        Assert.Equal("/shutdown", DriverEndpoints.All[^1]);
        Assert.Equal(EndpointsTheFrontendReaches.Length + 3, DriverEndpoints.All.Count);
    }

    private static StartSessionRequest LegacyRequest =>
        new()
        {
            SessionId = SessionId.Parse("rec-1"),
            Purpose = SessionPurpose.Recording,
            Tuning = new TuningRequest(TunerKind.Terrestrial, 55, 50001),
            DeviceId = "adapter0",
            OutputRoot = "primary",
            EndsAt = Moment,
        };

    private static SessionSnapshot LiveSession =>
        new(SessionId.Parse("s-1"), SessionPurpose.Live, "adapter1", SessionState.Active, Moment);

    private static IReadOnlyList<string> FieldsOf(string json)
    {
        using var document = JsonDocument.Parse(json);

        return [.. document.RootElement.EnumerateObject().Select(property => property.Name)];
    }
}
