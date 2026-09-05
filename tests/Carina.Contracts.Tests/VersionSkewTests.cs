using System.Text.Json.Nodes;

namespace Carina.Contracts.Tests;

public sealed class VersionSkewTests
{
    [Fact]
    public void AnAnswerWithoutTheNewerFieldsStillReads()
    {
        SessionSnapshot? session = DriverJson.Deserialize(
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
        SessionSnapshot? session = DriverJson.Deserialize(
            """{"sessionId":"s-1","purpose":"recording","deviceId":"a0","state":"stopped","startedAt":"2026-08-08T21:04:00+09:00","counters":null}""",
            DriverJson.Context.SessionSnapshot
        );

        Assert.NotNull(session);
        Assert.NotNull(session.Counters);
        Assert.False(session.Concluded);
    }

    [Fact]
    public void AHurriedSurveyReadsBackAsItself()
    {
        SessionSnapshot? session = DriverJson.Deserialize(
            """{"sessionId":"s-1","purpose":"surveyNow","deviceId":"a0","state":"active","startedAt":"2026-08-08T21:04:00+09:00"}""",
            DriverJson.Context.SessionSnapshot
        );

        Assert.NotNull(session);
        Assert.Equal(SessionPurpose.SurveyNow, session.Purpose);
    }

    [Fact]
    public void AnAnswerWithNewerFieldsAndValuesStillReads()
    {
        SessionSnapshot? session = DriverJson.Deserialize(
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
        TunerSnapshot? tuner = DriverJson.Deserialize(
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
        TunerSnapshot? tuner = DriverJson.Deserialize(
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
        DriverHello? hello = DriverJson.Deserialize(
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
        DriverHello? older = DriverJson.Deserialize(
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
        SessionSnapshot? session = DriverJson.Deserialize(
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
        StartSessionRequest? request = AsOlderDriverReadsIt(TuneParams.Terrestrial(55));

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
        TuneParams tune = system is TuneSystem.IsdbSBs ? TuneParams.Bs(15, 50001) : TuneParams.Cs110(24);

        StartSessionRequest? request = AsOlderDriverReadsIt(tune);

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
        TunerSnapshot? tuner = DriverJson.Deserialize(
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
        DriverHello? hello = DriverJson.Deserialize(
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
        DriverHello? hello = DriverJson.Deserialize(
            """{"protocolVersion":1,"instanceId":"old","capabilities":["recording"]}""",
            DriverJson.Context.DriverHello
        );

        Assert.NotNull(hello);
        Assert.False(hello.Supports(DriverCapabilities.LiveTunerToggle));
        Assert.False(hello.Supports(DriverCapabilities.SignalQuality));
        Assert.Empty(hello.DeclaredSignalQualityMetrics());
    }

    [Fact]
    public void DetectionAndTheLedgerAreNotAskedOfADriverThatDoesNotDeclareThem()
    {
        DriverHello? hello = DriverJson.Deserialize(
            """{"protocolVersion":1,"instanceId":"old","capabilities":["recording","live"]}""",
            DriverJson.Context.DriverHello
        );

        Assert.NotNull(hello);
        Assert.False(hello.Supports(DriverCapabilities.DeviceDetection));
        Assert.False(hello.Supports(DriverCapabilities.TunerLedger));
    }

    [Fact]
    public void APurposeThisBuildDoesNotKnowIsNotMistakenForAScan()
    {
        Assert.Equal(
            SessionPurpose.Unspecified,
            DriverJson.Deserialize("\"catchUp\"", DriverJson.Context.SessionPurpose)
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
        TunerConfigEntry? entry = DriverJson.Deserialize(
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
        TunerSnapshot? tuner = DriverJson.Deserialize(
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
        TunerSnapshot? tuner = DriverJson.Deserialize(
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
        TunerLedgerDto? ledger = DriverJson.Deserialize(
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
        TuneParams? tune = DriverJson.Deserialize(
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
        SignalQualityDto? reading = DriverJson.Deserialize(
            """{"lock":"locked","cnrMilliDecibels":21500,"signalStrengthMilliDecibels":-40000,"postViterbiBitErrors":[{"layer":0,"errorBits":12,"totalBits":1000000}]}""",
            DriverJson.Context.SignalQualityDto
        );

        Assert.NotNull(reading);
        Assert.Equal(21_500, reading.CnrMilliDecibels);
        Assert.Single(reading.PostViterbiBitErrors);
    }

    [Fact]
    public void TheBaselinePurposesAreTheOnesEveryDriverHasAlwaysAccepted()
    {
        Assert.Equal(
            PurposesTheOlderDriverKnows,
            SessionPurposes.Baseline.Select(SessionPurposeConverter.WireName)
        );
    }

    [Theory]
    [MemberData(nameof(PurposesThisBuildCanAskFor))]
    public void ADriverThatPredatesAPurposeIsOnlyEverAskedForOneItKnows(SessionPurpose purpose)
    {
        SessionPurpose agreed = SessionPurposes.AgreedWith(OlderDriver, purpose);

        string[] readable = [.. PurposesTheOlderDriverKnows, "unspecified"];

        Assert.Contains(SessionPurposeConverter.WireName(agreed), readable);
    }

    [Theory]
    [MemberData(nameof(PurposesThisBuildCanAskFor))]
    public void ThePurposeAnOlderDriverIsAskedForIsOneItStarts(SessionPurpose purpose)
    {
        SessionPurpose agreed = SessionPurposes.AgreedWith(OlderDriver, purpose);

        if (agreed is SessionPurpose.Unspecified)
        {
            Assert.NotNull(SessionPurposes.Capability(purpose));

            return;
        }

        StartSessionRequest? read = AsOlderDriverReadsThePurpose(agreed);

        Assert.NotNull(read);
        Assert.Equal(agreed, read.Purpose);
        Assert.Empty(read.Validate(Moment));
    }

    [Fact]
    public void AHurriedSurveyReachesADriverThatPredatesItAsAnOrdinaryOne()
    {
        SessionPurpose agreed = SessionPurposes.AgreedWith(OlderDriver, SessionPurpose.SurveyNow);

        Assert.Equal(SessionPurpose.Survey, agreed);
        Assert.True(SessionPurposes.ReadsEveryPacket(agreed));
    }

    [Fact]
    public void ADriverThatDeclaresTheHurriedSurveyIsAskedForItPlainly()
    {
        var declaring = new DriverHello(
            DriverProtocol.Version,
            "current",
            [DriverCapabilities.Purpose("surveyNow")]
        );

        Assert.Equal(
            SessionPurpose.SurveyNow,
            SessionPurposes.AgreedWith(declaring, SessionPurpose.SurveyNow)
        );
    }

    [Fact]
    public void APurposeAnOlderDriverDoesNotKnowIsRefusedRatherThanGuessedAt()
    {
        StartSessionRequest? read = AsOlderDriverReadsThePurpose(SessionPurpose.SurveyNow);

        Assert.NotNull(read);
        Assert.Equal(SessionPurpose.Unspecified, read.Purpose);
        Assert.Contains(
            read.Validate(Moment),
            problem => problem.StartsWith("purpose:", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void APurposeThisBuildCannotSpellIsNeverPutOnTheWire()
    {
        Assert.Equal(
            SessionPurpose.Unspecified,
            SessionPurposes.AgreedWith(OlderDriver, (SessionPurpose)99)
        );
    }

    [Fact]
    public void ARecordingFromADriverThatCannotCountContinuityIsNotReadAsMeasured()
    {
        SessionSnapshot session = RecordingAnsweredWith(
            """{"packets":50000,"drops":7,"scrambledPackets":3,"deviceOverflows":2}"""
        );

        RecordingSessionDto recording = RecordingSessionDto.Of(OlderDriver, session);

        Assert.False(recording.CcMeasured);
        Assert.Null(recording.CcDropped);
        Assert.Null(recording.CcTotal);
        Assert.False(recording.ScrambleMeasured);
        Assert.Null(recording.ScrambledPackets);
        Assert.Equal(9_400_000, recording.BytesWritten);
        Assert.Equal(2, recording.EovfCount);
    }

    [Fact]
    public void ARecordingThatCountedNothingCarriesNoCountRatherThanAZero()
    {
        SessionSnapshot session = RecordingAnsweredWith(
            """{"packets":50000,"drops":7,"scrambledPackets":3,"deviceOverflows":2}"""
        );

        RecordingSessionDto recording = RecordingSessionDto.Of(OlderDriver, session);

        Assert.Equal(
            """{"sessionId":"rec-1","recordingId":"k-90210","outputRoot":"primary","startedAt":"2026-08-08T21:04:00+09:00","endsAt":null,"bytesWritten":9400000,"ccDropped":null,"ccTotal":null,"ccMeasured":false,"scrambledPackets":null,"scrambleMeasured":false,"eovfCount":2,"positions":null}""",
            DriverJson.Serialize(recording)
        );
    }

    [Fact]
    public void ARecordingThatCountedAndFoundNothingIsNotReadAsUnmeasured()
    {
        SessionSnapshot session = RecordingAnsweredWith(
            """{"packets":50000,"drops":0,"scrambledPackets":0,"deviceOverflows":0,"ccMeasured":true,"scrambleMeasured":true}"""
        );

        RecordingSessionDto recording = RecordingSessionDto.Of(MeasuringDriver, session);

        Assert.True(recording.CcMeasured);
        Assert.Equal(0, recording.CcDropped);
        Assert.Equal(50_000, recording.CcTotal);
        Assert.True(recording.ScrambleMeasured);
        Assert.Equal(0, recording.ScrambledPackets);
        Assert.Equal(
            """{"sessionId":"rec-1","recordingId":"k-90210","outputRoot":"primary","startedAt":"2026-08-08T21:04:00+09:00","endsAt":null,"bytesWritten":9400000,"ccDropped":0,"ccTotal":50000,"ccMeasured":true,"scrambledPackets":0,"scrambleMeasured":true,"eovfCount":0,"positions":null}""",
            DriverJson.Serialize(recording)
        );
    }

    [Fact]
    public void ADriverThatDoesNotDeclareTheCountIsNotTakenAtItsWordAboutHavingMadeIt()
    {
        SessionSnapshot session = RecordingAnsweredWith(
            """{"packets":50000,"drops":7,"scrambledPackets":3,"ccMeasured":true,"scrambleMeasured":true}"""
        );

        RecordingSessionDto recording = RecordingSessionDto.Of(OlderDriver, session);

        Assert.False(recording.CcMeasured);
        Assert.Null(recording.CcTotal);
        Assert.False(recording.ScrambleMeasured);
        Assert.Null(recording.ScrambledPackets);
    }

    [Fact]
    public void ADriverThatCountsContinuityAndNotScramblingDegradesOnlyTheCountItCannotMake()
    {
        SessionSnapshot session = RecordingAnsweredWith(
            """{"packets":50000,"drops":7,"scrambledPackets":3,"deviceOverflows":2,"ccMeasured":true,"scrambleMeasured":true}"""
        );

        RecordingSessionDto recording = RecordingSessionDto.Of(ContinuityOnlyDriver, session);

        Assert.True(recording.CcMeasured);
        Assert.Equal(7, recording.CcDropped);
        Assert.Equal(50_007, recording.CcTotal);
        Assert.False(recording.ScrambleMeasured);
        Assert.Null(recording.ScrambledPackets);
        Assert.Equal(2, recording.EovfCount);
    }

    [Fact]
    public void ADriverThatCountsScramblingAndNotContinuityDegradesOnlyTheCountItCannotMake()
    {
        SessionSnapshot session = RecordingAnsweredWith(
            """{"packets":50000,"drops":7,"scrambledPackets":3,"deviceOverflows":2,"ccMeasured":true,"scrambleMeasured":true}"""
        );

        RecordingSessionDto recording = RecordingSessionDto.Of(ScrambleOnlyDriver, session);

        Assert.False(recording.CcMeasured);
        Assert.Null(recording.CcDropped);
        Assert.Null(recording.CcTotal);
        Assert.True(recording.ScrambleMeasured);
        Assert.Equal(3, recording.ScrambledPackets);
        Assert.Equal(2, recording.EovfCount);
    }

    [Fact]
    public void ARecordingCountedForContinuityAndNotForScramblingCarriesOnlyTheCountItMade()
    {
        SessionSnapshot session = RecordingAnsweredWith(
            """{"packets":50000,"drops":7,"scrambledPackets":3,"ccMeasured":true}"""
        );

        RecordingSessionDto recording = RecordingSessionDto.Of(MeasuringDriver, session);

        Assert.True(recording.CcMeasured);
        Assert.Equal(7, recording.CcDropped);
        Assert.False(recording.ScrambleMeasured);
        Assert.Null(recording.ScrambledPackets);
    }

    [Fact]
    public void ARecordingCountedForScramblingAndNotForContinuityCarriesOnlyTheCountItMade()
    {
        SessionSnapshot session = RecordingAnsweredWith(
            """{"packets":50000,"drops":7,"scrambledPackets":3,"scrambleMeasured":true}"""
        );

        RecordingSessionDto recording = RecordingSessionDto.Of(MeasuringDriver, session);

        Assert.False(recording.CcMeasured);
        Assert.Null(recording.CcDropped);
        Assert.Null(recording.CcTotal);
        Assert.True(recording.ScrambleMeasured);
        Assert.Equal(3, recording.ScrambledPackets);
    }

    [Fact]
    public void ADriverThatCanCountButSaidNothingOfThisRecordingHasNotCountedIt()
    {
        SessionSnapshot session = RecordingAnsweredWith(
            """{"packets":50000,"drops":0,"scrambledPackets":0}"""
        );

        RecordingSessionDto recording = RecordingSessionDto.Of(MeasuringDriver, session);

        Assert.False(recording.CcMeasured);
        Assert.Null(recording.CcTotal);
        Assert.False(recording.ScrambleMeasured);
    }

    [Fact]
    public void ACountThatFaultedPartWayThroughIsNotReadAsACleanZero()
    {
        SessionSnapshot session = RecordingAnsweredWith(
            """{"packets":50000,"drops":0,"scrambledPackets":0,"ccMeasured":false,"scrambleMeasured":false}"""
        );

        RecordingSessionDto recording = RecordingSessionDto.Of(MeasuringDriver, session);

        Assert.False(recording.CcMeasured);
        Assert.Null(recording.CcTotal);
        Assert.False(recording.ScrambleMeasured);
    }

    [Fact]
    public void ADriverThatCannotSayWhereTheLossesWereHasItsPositionsLeftBehind()
    {
        SessionSnapshot session = RecordingAnsweredWith(
            """{"packets":50000,"drops":7,"ccMeasured":true,"scrambleMeasured":true,"positions":{"anchorPcr":900,"buckets":[{"second":4,"continuity":7,"scrambled":0}],"reanchors":[]}}"""
        );

        RecordingSessionDto recording = RecordingSessionDto.Of(MeasuringDriver, session);

        Assert.True(recording.CcMeasured);
        Assert.Equal(7, recording.CcDropped);
        Assert.Null(recording.Positions);
    }

    public static TheoryData<string, bool, bool, bool> OneLegAtATime() =>
        new()
        {
            { "everything", true, true, true },
            { "capability", false, true, true },
            { "continuity", true, false, true },
            { "scrambling", true, true, false },
        };

    [Theory]
    [MemberData(nameof(OneLegAtATime))]
    public void APositionRidesOnTheDeclaredCapabilityAndOnBothCountsSeparately(
        string leg,
        bool declaresPositions,
        bool countsContinuity,
        bool countsScrambling
    )
    {
        var capabilities = new List<string> { DriverCapabilities.Recording };

        if (declaresPositions)
        {
            capabilities.Add(DriverCapabilities.DropPositions);
        }

        if (countsContinuity)
        {
            capabilities.Add(DriverCapabilities.CcMeasurement);
        }

        if (countsScrambling)
        {
            capabilities.Add(DriverCapabilities.ScrambleMeasurement);
        }

        string counted = countsContinuity ? "true" : "false";
        string unresolved = countsScrambling ? "true" : "false";
        SessionSnapshot session = RecordingAnsweredWith(
            @"{""packets"":50000,""drops"":7,""scrambledPackets"":2,""ccMeasured"":"
                + counted
                + @",""scrambleMeasured"":"
                + unresolved
                + @",""positions"":{""anchorPcr"":900,""buckets"":[],""reanchors"":[]}}"
        );

        RecordingSessionDto recording = RecordingSessionDto.Of(
            new DriverHello(DriverProtocol.Version, leg, capabilities),
            session
        );

        Assert.Equal(leg is "everything", recording.Positions is not null);
    }

    [Fact]
    public void ADriverThatSaysWhereTheLossesWereIsBelievedAboutTheSecondsItNames()
    {
        SessionSnapshot session = RecordingAnsweredWith(
            """{"packets":50000,"drops":9,"scrambledPackets":2,"ccMeasured":true,"scrambleMeasured":true,"positions":{"anchorPcr":900,"buckets":[{"second":4,"continuity":7,"scrambled":0},{"second":11,"continuity":2,"scrambled":2}],"reanchors":[{"second":8,"before":123,"after":456}]}}"""
        );

        RecordingSessionDto recording = RecordingSessionDto.Of(LocatingDriver, session);

        Assert.NotNull(recording.Positions);
        Assert.Equal(900, recording.Positions.AnchorPcr);
        Assert.Equal([4, 11], recording.Positions.Buckets.Select(bucket => bucket.Second));
        Assert.Equal([7, 2], recording.Positions.Buckets.Select(bucket => bucket.Continuity));
        Assert.Equal([0, 2], recording.Positions.Buckets.Select(bucket => bucket.Scrambled));
        Assert.Equal([8], recording.Positions.Reanchors.Select(reanchor => reanchor.Second));
        Assert.Equal(123, recording.Positions.Reanchors[0].Before);
        Assert.Equal(456, recording.Positions.Reanchors[0].After);
    }

    [Fact]
    public void ACountWithNoPositionAtAllIsNotTheSameAsAPositionWithNothingInIt()
    {
        SessionSnapshot located = RecordingAnsweredWith(
            """{"packets":50000,"drops":0,"ccMeasured":true,"scrambleMeasured":true,"positions":{"anchorPcr":900,"buckets":[],"reanchors":[]}}"""
        );
        SessionSnapshot unlocated = RecordingAnsweredWith(
            """{"packets":50000,"drops":0,"ccMeasured":true,"scrambleMeasured":true}"""
        );

        RecordingSessionDto withAPosition = RecordingSessionDto.Of(LocatingDriver, located);
        RecordingSessionDto withNone = RecordingSessionDto.Of(LocatingDriver, unlocated);

        Assert.True(withAPosition.CcMeasured);
        Assert.NotNull(withAPosition.Positions);
        Assert.Empty(withAPosition.Positions.Buckets);
        Assert.Equal(900, withAPosition.Positions.AnchorPcr);

        Assert.True(withNone.CcMeasured);
        Assert.Null(withNone.Positions);
    }

    [Fact]
    public void APositionFromADriverThatDoesNotCountContinuityIsLeftBehind()
    {
        SessionSnapshot session = RecordingAnsweredWith(
            """{"packets":50000,"drops":7,"scrambleMeasured":true,"positions":{"anchorPcr":900,"buckets":[],"reanchors":[]}}"""
        );

        Assert.Null(RecordingSessionDto.Of(PositionsWithoutContinuityDriver, session).Positions);
    }

    [Fact]
    public void APositionFromADriverThatDoesNotCountScramblingIsLeftBehind()
    {
        SessionSnapshot session = RecordingAnsweredWith(
            """{"packets":50000,"drops":7,"ccMeasured":true,"positions":{"anchorPcr":900,"buckets":[{"second":3,"continuity":0,"scrambled":4}],"reanchors":[]}}"""
        );

        Assert.Null(RecordingSessionDto.Of(PositionsWithoutScrambleDriver, session).Positions);
    }

    [Fact]
    public void APositionOnCountersThatSayNothingWasCountedIsLeftBehind()
    {
        SessionSnapshot session = RecordingAnsweredWith(
            """{"packets":50000,"drops":7,"ccMeasured":false,"scrambleMeasured":false,"positions":{"anchorPcr":900,"buckets":[],"reanchors":[]}}"""
        );

        RecordingSessionDto recording = RecordingSessionDto.Of(LocatingDriver, session);

        Assert.False(recording.CcMeasured);
        Assert.Null(recording.CcDropped);
        Assert.Null(recording.Positions);
    }

    [Fact]
    public void TheTotalOnTheWireIsWhatTheStreamShouldHaveCarried()
    {
        SessionSnapshot session = RecordingAnsweredWith(
            """{"packets":40,"drops":117,"ccMeasured":true,"scrambleMeasured":true}"""
        );

        RecordingSessionDto recording = RecordingSessionDto.Of(LocatingDriver, session);

        Assert.Equal(117, recording.CcDropped);
        Assert.Equal(157, recording.CcTotal);
    }

    [Fact]
    public void ARecordingFromADriverThatPredatesAllOfThisStillReadsForWhatItDidSay()
    {
        SessionSnapshot? session = DriverJson.Deserialize(
            """{"sessionId":"rec-1","purpose":"recording","deviceId":"a0","state":"active","startedAt":"2026-08-08T21:04:00+09:00","outputRoot":"primary","bytesRecorded":9400000}""",
            DriverJson.Context.SessionSnapshot
        );

        Assert.NotNull(session);
        Assert.Null(session.RecordingId);

        RecordingSessionDto recording = RecordingSessionDto.Of(OlderDriver, session);

        Assert.Equal(string.Empty, recording.RecordingId);
        Assert.Equal("primary", recording.OutputRoot);
        Assert.Equal(9_400_000, recording.BytesWritten);
        Assert.False(recording.CcMeasured);
    }

    [Fact]
    public void ARecordingIdIsIgnoredByADriverThatNeverHeardOfIt()
    {
        string json = DriverJson.Serialize(
            new StartSessionRequest
            {
                SessionId = SessionId.Parse("rec-1"),
                Purpose = SessionPurpose.Recording,
                Tuning = new TuningRequest(TunerKind.Terrestrial, 55, 50001),
                OutputRoot = "primary",
                EndsAt = Moment.AddHours(1),
                RecordingId = "k-90210",
            }
        );

        JsonObject body = JsonNode.Parse(json)!.AsObject();
        body.Remove("recordingId");

        StartSessionRequest? read = DriverJson.Deserialize(
            body.ToJsonString(),
            DriverJson.Context.StartSessionRequest
        );

        Assert.NotNull(read);
        Assert.Equal("primary", read.OutputRoot);
        Assert.Equal(SessionPurpose.Recording, read.Purpose);
    }

    [Fact]
    public void ADriverThatPredatesTheExtensionIsNotAskedToFollowAProgramme()
    {
        Assert.False(OlderDriver.Supports(DriverCapabilities.RecordingExtension));
        Assert.False(OlderDriver.Supports(DriverCapabilities.Storage));
        Assert.True(MeasuringDriver.Supports(DriverCapabilities.RecordingExtension));
        Assert.True(MeasuringDriver.Supports(DriverCapabilities.Storage));
    }

    [Fact]
    public void AnExtensionOnlyEverMovesTheEndLater()
    {
        var request = new ExtendSessionRequest { EndsAt = Moment.AddMinutes(10) };

        Assert.Empty(request.Validate(Moment));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void AnExtensionThatWouldCutARecordingShortIsRefused(int minutes)
    {
        var request = new ExtendSessionRequest { EndsAt = Moment.AddMinutes(minutes) };

        Assert.Contains(
            request.Validate(Moment),
            problem => problem.StartsWith("endsAt:", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void ARootThisDriverNeverDeclaredIsNotOneARecordingMayBeSentTo()
    {
        IReadOnlyList<StorageRootDto> roots =
        [
            new StorageRootDto { Name = "primary", FreeBytes = 1024, Writable = true },
        ];

        Assert.True(StorageRoots.Declares(roots, "primary"));
        Assert.False(StorageRoots.Declares(roots, "secondary"));
        Assert.False(StorageRoots.Declares(roots, "PRIMARY"));
        Assert.False(StorageRoots.Declares([], "primary"));
    }

    [Fact]
    public void ARootWhoseNameNeverArrivedIsNotTheRootNobodyNamed()
    {
        IReadOnlyList<StorageRootDto>? roots = DriverJson.Deserialize(
            """[{"name":null,"freeBytes":1024,"totalBytes":2048,"writable":true}]""",
            DriverJson.Context.IReadOnlyListStorageRootDto
        );

        Assert.NotNull(roots);
        Assert.Null(Assert.Single(roots).Name);
        Assert.False(StorageRoots.Declares(roots, null));
    }

    private static SessionSnapshot RecordingAnsweredWith(string counters)
    {
        SessionSnapshot? session = DriverJson.Deserialize(
            $$"""{"sessionId":"rec-1","purpose":"recording","deviceId":"a0","state":"active","startedAt":"2026-08-08T21:04:00+09:00","outputRoot":"primary","recordingId":"k-90210","bytesRecorded":9400000,"counters":{{counters}}}""",
            DriverJson.Context.SessionSnapshot
        );

        Assert.NotNull(session);

        return session;
    }

    public static TheoryData<SessionPurpose> PurposesThisBuildCanAskFor()
    {
        var purposes = new TheoryData<SessionPurpose>();

        foreach (SessionPurpose purpose in Enum.GetValues<SessionPurpose>())
        {
            if (purpose is not SessionPurpose.Unspecified)
            {
                purposes.Add(purpose);
            }
        }

        return purposes;
    }

    private static StartSessionRequest? AsOlderDriverReadsThePurpose(SessionPurpose purpose)
    {
        var tune = TuneParams.Terrestrial(55);
        bool records = purpose is SessionPurpose.Recording;
        string json = DriverJson.Serialize(
            new StartSessionRequest
            {
                SessionId = SessionId.Parse("epg-1"),
                Purpose = purpose,
                Tuning = tune.ToLegacyRequest(),
                Tune = tune,
                OutputRoot = records ? "primary" : null,
                EndsAt = records ? Moment.AddHours(1) : null,
                RecordingId = records ? "k-90210" : null,
            }
        );

        JsonObject body = JsonNode.Parse(json)!.AsObject();
        string spelling = body["purpose"]!.GetValue<string>();

        if (!PurposesTheOlderDriverKnows.Contains(spelling, StringComparer.Ordinal))
        {
            body["purpose"] = "unspecified";
        }

        return DriverJson.Deserialize(
            body.ToJsonString(),
            DriverJson.Context.StartSessionRequest
        );
    }

    private static StartSessionRequest? AsOlderDriverReadsIt(TuneParams tune)
    {
        string json = DriverJson.Serialize(
            new StartSessionRequest
            {
                SessionId = SessionId.Parse("scan-1"),
                Purpose = SessionPurpose.Live,
                Tuning = tune.ToLegacyRequest(),
                Tune = tune,
            }
        );

        JsonObject body = JsonNode.Parse(json)!.AsObject();
        body.Remove("tune");

        return DriverJson.Deserialize(
            body.ToJsonString(),
            DriverJson.Context.StartSessionRequest
        );
    }

    private static readonly DateTimeOffset Moment =
        new(2026, 8, 8, 21, 4, 0, TimeSpan.FromHours(9));

    private static readonly string[] PurposesTheOlderDriverKnows =
        ["recording", "live", "survey", "scan"];

    private static readonly DriverHello OlderDriver =
        new(
            DriverProtocol.Version,
            "older",
            [DriverCapabilities.Recording, DriverCapabilities.Live]
        );

    private static readonly DriverHello ContinuityOnlyDriver =
        new(
            DriverProtocol.Version,
            "continuity-only",
            [
                DriverCapabilities.Recording,
                DriverCapabilities.Live,
                DriverCapabilities.CcMeasurement,
            ]
        );

    private static readonly DriverHello ScrambleOnlyDriver =
        new(
            DriverProtocol.Version,
            "scramble-only",
            [
                DriverCapabilities.Recording,
                DriverCapabilities.Live,
                DriverCapabilities.ScrambleMeasurement,
            ]
        );

    private static readonly DriverHello PositionsWithoutContinuityDriver =
        new(
            DriverProtocol.Version,
            "positions-no-continuity",
            [
                DriverCapabilities.Recording,
                DriverCapabilities.ScrambleMeasurement,
                DriverCapabilities.DropPositions,
            ]
        );

    private static readonly DriverHello PositionsWithoutScrambleDriver =
        new(
            DriverProtocol.Version,
            "positions-no-scramble",
            [
                DriverCapabilities.Recording,
                DriverCapabilities.CcMeasurement,
                DriverCapabilities.DropPositions,
            ]
        );

    private static readonly DriverHello LocatingDriver =
        new(
            DriverProtocol.Version,
            "locating",
            [
                DriverCapabilities.Recording,
                DriverCapabilities.Live,
                DriverCapabilities.CcMeasurement,
                DriverCapabilities.ScrambleMeasurement,
                DriverCapabilities.DropPositions,
            ]
        );

    private static readonly DriverHello MeasuringDriver =
        new(
            DriverProtocol.Version,
            "current",
            [
                DriverCapabilities.Recording,
                DriverCapabilities.Live,
                DriverCapabilities.CcMeasurement,
                DriverCapabilities.ScrambleMeasurement,
                DriverCapabilities.RecordingExtension,
                DriverCapabilities.Storage,
            ]
        );
}
