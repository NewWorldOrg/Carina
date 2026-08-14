using System.Text.Json.Nodes;

namespace Carina.Contracts.Tests;

public sealed class VersionSkewTests
{
    [Fact]
    public void AnAnswerWithoutTheNewerFieldsStillReads()
    {
        var session = DriverJson.Deserialize(
            """{"sessionId":"s-1","purpose":"recording","deviceId":"a0","state":"active","startedAt":"2026-08-08T21:04:00+09:00"}""",
            DriverJson.Context.SessionSnapshot
        );

        Assert.NotNull(session);
        Assert.Null(session.EndsAt);
        Assert.Equal(SessionPurpose.Recording, session.Purpose);
        Assert.Equal(SessionStopReason.Unspecified, session.StopReason);
        Assert.False(session.Concluded);
        Assert.Equal(SessionCounters.Nothing, session.Counters);
    }

    [Fact]
    public void ASessionWithoutCountersIsStillReadable()
    {
        var session = DriverJson.Deserialize(
            """{"sessionId":"s-1","purpose":"recording","deviceId":"a0","state":"stopped","startedAt":"2026-08-08T21:04:00+09:00","counters":null}""",
            DriverJson.Context.SessionSnapshot
        );

        Assert.NotNull(session);
        Assert.NotNull(session.Counters);
        Assert.False(session.Concluded);
    }

    [Fact]
    public void AnAnswerWithNewerFieldsAndValuesStillReads()
    {
        var session = DriverJson.Deserialize(
            """{"sessionId":"s-1","purpose":"epgNow","deviceId":"a0","state":"draining","startedAt":"2026-08-08T21:04:00+09:00","priority":8}""",
            DriverJson.Context.SessionSnapshot
        );

        Assert.NotNull(session);
        Assert.Equal(SessionPurpose.Unspecified, session.Purpose);
        Assert.Equal(SessionState.Unspecified, session.State);
        Assert.Equal("a0", session.DeviceId);
    }

    [Fact]
    public void AnUnknownTunerStateDoesNotReadAsAWorkingTuner()
    {
        var tuner = DriverJson.Deserialize(
            """{"deviceId":"a0","kind":"terrestrial","state":"warmingUp"}""",
            DriverJson.Context.TunerSnapshot
        );

        Assert.NotNull(tuner);
        Assert.Equal(TunerState.Unspecified, tuner.State);
        Assert.NotEqual(TunerState.Idle, tuner.State);
    }

    [Fact]
    public void NumericEnumValuesAreNotHonoured()
    {
        var tuner = DriverJson.Deserialize(
            """{"deviceId":"a0","kind":1,"state":2}""",
            DriverJson.Context.TunerSnapshot
        );

        Assert.NotNull(tuner);
        Assert.Equal(TunerKind.Unspecified, tuner.Kind);
        Assert.Equal(TunerState.Unspecified, tuner.State);
    }

    [Fact]
    public void ADriverWithoutACapabilityIsUsableForEverythingElse()
    {
        var hello = DriverJson.Deserialize(
            """{"protocolVersion":1,"instanceId":"old","capabilities":["recording"]}""",
            DriverJson.Context.DriverHello
        );

        Assert.NotNull(hello);
        Assert.True(hello.Supports(DriverCapabilities.Recording));
        Assert.False(hello.Supports(DriverCapabilities.QualityMetering));
    }

    [Fact]
    public void ARestartIsTheChangeOfInstanceAndNothingElse()
    {
        var first = new DriverHello(1, "b7f2c9", []);
        var reconnected = new DriverHello(1, "b7f2c9", [DriverCapabilities.Live]);
        var restarted = new DriverHello(1, "3ad901", []);

        Assert.False(reconnected.IsDifferentInstanceFrom(first));
        Assert.True(restarted.IsDifferentInstanceFrom(first));
        Assert.True(first.IsDifferentInstanceFrom(null));
    }

    [Fact]
    public void ADriverThatNamesNoInstanceIsAlwaysTreatedAsARestart()
    {
        var older = DriverJson.Deserialize(
            """{"protocolVersion":1,"capabilities":["recording"]}""",
            DriverJson.Context.DriverHello
        );

        Assert.NotNull(older);
        Assert.Null(older.InstanceId);
        Assert.True(older.IsDifferentInstanceFrom(older));
        Assert.True(older.IsDifferentInstanceFrom(new DriverHello(1, "b7f2c9", [])));
        Assert.True(new DriverHello(1, "b7f2c9", []).IsDifferentInstanceFrom(older));
    }

    [Fact]
    public void AnIdentifierOutsideTheShapeLeavesTheRestOfTheAnswerReadable()
    {
        var session = DriverJson.Deserialize(
            """{"sessionId":"../x","purpose":"live","deviceId":"a0","state":"active","startedAt":"2026-08-08T21:04:00+09:00"}""",
            DriverJson.Context.SessionSnapshot
        );

        Assert.NotNull(session);
        Assert.True(session.SessionId.IsUnset);
        Assert.Equal("a0", session.DeviceId);
        Assert.Equal(SessionState.Active, session.State);
    }

    [Fact]
    public void AnUnsetIdentifierHasNoPath()
    {
        Assert.Throws<ArgumentException>(() => DriverEndpoints.Session(default));
        Assert.Throws<ArgumentException>(() => DriverEndpoints.SessionStream(default));
    }

    [Fact]
    public void ATerrestrialTuneReachesADriverThatNeverHeardOfTheTypedShape()
    {
        var request = AsOlderDriverReadsIt(TuneParams.Terrestrial(55));

        Assert.NotNull(request);
        Assert.Null(request.Tune);
        Assert.Equal(55, request.Tuning.PhysicalChannel);
        Assert.Empty(request.Validate(Moment));
    }

    [Theory]
    [InlineData(TuneSystem.IsdbSBs)]
    [InlineData(TuneSystem.IsdbSCs110)]
    public void ASatelliteTuneIsRefusedByADriverThatCannotTellTheTwoApart(TuneSystem system)
    {
        var tune = system is TuneSystem.IsdbSBs ? TuneParams.Bs(15, 50001) : TuneParams.Cs110(24);

        var request = AsOlderDriverReadsIt(tune);

        Assert.NotNull(request);
        Assert.Equal(TunerKind.Unspecified, request.Tuning.Kind);
        Assert.Contains(
            request.Validate(Moment),
            problem => problem.StartsWith("tuning.kind:", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void BothFieldsTravelTogetherAndNameTheSameTune()
    {
        var tune = TuneParams.Terrestrial(55);
        var request = new StartSessionRequest
        {
            SessionId = SessionId.Parse("scan-1"),
            Purpose = SessionPurpose.Scan,
            Tuning = tune.ToLegacyRequest(),
            Tune = tune,
        };

        Assert.Equal(TunerKind.Terrestrial, request.Tuning.Kind);
        Assert.Equal(55, request.Tuning.PhysicalChannel);
        Assert.Equal(55, request.Tune?.IsdbT?.PhysicalChannel);
        Assert.Empty(request.Validate(Moment));
    }

    [Fact]
    public void ATunerAnsweredByADriverWithoutAnyOfThisIsStillReadable()
    {
        var tuner = DriverJson.Deserialize(
            """{"deviceId":"a0","kind":"terrestrial","state":"busy","sessionId":"s-1"}""",
            DriverJson.Context.TunerSnapshot
        );

        Assert.NotNull(tuner);
        Assert.Null(tuner.Health);
        Assert.Null(tuner.SignalQuality);
        Assert.Null(tuner.CurrentSession);
        Assert.Equal(TunerState.Busy, tuner.State);
    }

    [Fact]
    public void ADriverThatMeasuresNothingDegradesOneMetricAtATime()
    {
        var hello = DriverJson.Deserialize(
            """{"protocolVersion":1,"instanceId":"old","capabilities":["recording","signalQuality","signalQuality.cnr"]}""",
            DriverJson.Context.DriverHello
        );

        Assert.NotNull(hello);
        Assert.True(hello.Supports(DriverCapabilities.SignalQuality));
        Assert.True(hello.SupportsSignalQualityMetric(SignalQualityMetrics.Cnr));
        Assert.False(
            hello.SupportsSignalQualityMetric(SignalQualityMetrics.PostViterbiBitError)
        );
        Assert.False(hello.SupportsSignalQualityMetric("signalStrength"));
    }

    [Fact]
    public void ATunerToggleIsNotAttemptedAgainstADriverThatCannotDoIt()
    {
        var hello = DriverJson.Deserialize(
            """{"protocolVersion":1,"instanceId":"old","capabilities":["recording"]}""",
            DriverJson.Context.DriverHello
        );

        Assert.NotNull(hello);
        Assert.False(hello.Supports(DriverCapabilities.LiveTunerToggle));
        Assert.False(hello.Supports(DriverCapabilities.SignalQuality));
        Assert.Empty(hello.DeclaredSignalQualityMetrics());
    }

    [Fact]
    public void APurposeThisBuildDoesNotKnowIsNotMistakenForAScan()
    {
        Assert.Equal(
            SessionPurpose.Unspecified,
            DriverJson.Deserialize("\"logo\"", DriverJson.Context.SessionPurpose)
        );
        Assert.Equal(
            SessionPurpose.Scan,
            DriverJson.Deserialize("\"scan\"", DriverJson.Context.SessionPurpose)
        );
    }

    [Fact]
    public void AnEventNameThisBuildDoesNotKnowIsHarmless()
    {
        Assert.False(DriverEvents.IsKnown("tunerRetired"));
        Assert.True(DriverEvents.IsKnown(DriverEvents.SessionLockLost));
    }

    [Fact]
    public void ALedgerEntryFromANewerAppKeepsWhatThisBuildUnderstands()
    {
        var entry = DriverJson.Deserialize(
            """{"deviceId":"adapter0","disabled":true,"lnbPower":true,"lnbVoltage":15}""",
            DriverJson.Context.TunerConfigEntry
        );

        Assert.NotNull(entry);
        Assert.True(entry.Disabled);
        Assert.True(entry.LnbPower);
        Assert.Empty(entry.Validate());
    }

    [Fact]
    public void ADrainingTunerIsNotReadAsAWorkingOneByABuildThatPredatesTheState()
    {
        var tuner = DriverJson.Deserialize(
            """{"deviceId":"adapter0","kind":"terrestrial","state":"draining"}""",
            DriverJson.Context.TunerSnapshot
        );

        Assert.NotNull(tuner);
        Assert.Equal(TunerState.Draining, tuner.State);
        Assert.NotEqual(TunerState.Idle, tuner.State);
    }

    [Fact]
    public void ATunerFromADriverThatCannotTurnOneOffAtRuntimeIsNotReadAsHavingBeenToggled()
    {
        var tuner = DriverJson.Deserialize(
            """{"deviceId":"adapter0","kind":"terrestrial","state":"disabled"}""",
            DriverJson.Context.TunerSnapshot
        );

        Assert.NotNull(tuner);
        Assert.Equal(TunerState.Disabled, tuner.State);
        Assert.False(tuner.Toggled);
    }

    [Fact]
    public void ALedgerAnswerWithFieldsThisBuildDoesNotKnowStillReadsItsHashes()
    {
        var ledger = DriverJson.Deserialize(
            """{"tuners":[],"loadedHash":"aaaa","savedHash":"bbbb","savedAt":"2026-08-08T21:04:00+09:00"}""",
            DriverJson.Context.TunerLedgerDto
        );

        Assert.NotNull(ledger);
        Assert.Equal("aaaa", ledger.LoadedHash);
        Assert.Equal("bbbb", ledger.SavedHash);
        Assert.True(ledger.HasDrifted());
    }

    [Fact]
    public void ATuneArmThisBuildDoesNotKnowLeavesNothingToActOn()
    {
        var tune = DriverJson.Deserialize(
            """{"system":"isdbSSky","isdbSSky":{"transponder":3}}""",
            DriverJson.Context.TuneParams
        );

        Assert.NotNull(tune);
        Assert.Equal(TuneSystem.Unspecified, tune.System);
        Assert.Null(tune.IsdbT);
        Assert.Null(tune.IsdbSBs);
        Assert.Null(tune.IsdbSCs110);
        Assert.Equal(
            ["system: missing, or a value this driver does not know."],
            tune.Validate()
        );
    }

    [Fact]
    public void AQualitySubtreeFromANewerDriverIsReadForWhatThisBuildKnows()
    {
        var reading = DriverJson.Deserialize(
            """{"lock":"locked","cnrMilliDecibels":21500,"signalStrengthMilliDecibels":-40000,"postViterbiBitErrors":[{"layer":0,"errorBits":12,"totalBits":1000000}]}""",
            DriverJson.Context.SignalQualityDto
        );

        Assert.NotNull(reading);
        Assert.Equal(21_500, reading.CnrMilliDecibels);
        Assert.Single(reading.PostViterbiBitErrors);
    }

    private static StartSessionRequest? AsOlderDriverReadsIt(TuneParams tune)
    {
        var json = DriverJson.Serialize(
            new StartSessionRequest
            {
                SessionId = SessionId.Parse("scan-1"),
                Purpose = SessionPurpose.Live,
                Tuning = tune.ToLegacyRequest(),
                Tune = tune,
            }
        );

        var body = JsonNode.Parse(json)!.AsObject();
        body.Remove("tune");

        return DriverJson.Deserialize(
            body.ToJsonString(),
            DriverJson.Context.StartSessionRequest
        );
    }

    private static readonly DateTimeOffset Moment =
        new(2026, 8, 8, 21, 4, 0, TimeSpan.FromHours(9));
}
