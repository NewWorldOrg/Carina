namespace Carina.Contracts.Tests;

public sealed class TunerStatusTests
{
    private static readonly DateTimeOffset Moment =
        new(2026, 8, 8, 21, 4, 0, TimeSpan.FromHours(9));

    private static TunerSnapshot Idle =>
        new("adapter0", TunerKind.Terrestrial, TunerState.Idle);

    [Fact]
    public void ADriverThatReportsNoneOfThisLeavesTheSubtreesEmpty()
    {
        var tuner = DriverJson.Deserialize(
            """{"deviceId":"adapter0","kind":"terrestrial","state":"idle"}""",
            DriverJson.Context.TunerSnapshot
        );

        Assert.NotNull(tuner);
        Assert.Null(tuner.Health);
        Assert.Null(tuner.SignalQuality);
        Assert.Null(tuner.CurrentSession);
        Assert.Equal(TunerState.Idle, tuner.State);
    }

    [Fact]
    public void ATunerBeingTakenOutOfServiceWhileItWorksSaysBothThings()
    {
        var tuner = Idle with
        {
            State = TunerState.Busy,
            Health = new TunerHealthDto
            {
                Level = TunerHealthLevel.Healthy,
                DisablePending = true,
                ChangedAt = Moment,
            },
        };

        Assert.Equal(TunerState.Busy, tuner.State);
        Assert.True(tuner.Health?.DisablePending);
    }

    [Fact]
    public void AHealthSubtreeSaysWhetherThereIsPowerOnTheCable()
    {
        var health = new TunerHealthDto
        {
            Level = TunerHealthLevel.Faulted,
            LnbPowered = true,
            Detail = "the kind on this adapter is not the kind the ledger names",
        };

        Assert.Equal(
            """{"level":"faulted","disablePending":false,"lnbPowered":true,"detail":"the kind on this adapter is not the kind the ledger names","changedAt":null}""",
            DriverJson.Serialize(health)
        );
    }

    [Fact]
    public void AHealthLevelThisBuildDoesNotKnowIsNotReadAsHealthy()
    {
        var health = DriverJson.Deserialize(
            """{"level":"retiring"}""",
            DriverJson.Context.TunerHealthDto
        );

        Assert.NotNull(health);
        Assert.Equal(TunerHealthLevel.Unspecified, health.Level);
        Assert.NotEqual(TunerHealthLevel.Healthy, health.Level);
    }

    [Fact]
    public void TheSessionOnATunerNamesWhatItIsForAndWhatItIsOn()
    {
        var tuner = Idle with
        {
            State = TunerState.Busy,
            SessionId = SessionId.Parse("scan-1"),
            CurrentSession = new CurrentSessionDto
            {
                SessionId = SessionId.Parse("scan-1"),
                Purpose = SessionPurpose.Scan,
                StartedAt = Moment,
                Tune = TuneParams.Terrestrial(27),
            },
        };

        var restored = DriverJson.Deserialize(
            DriverJson.Serialize(tuner),
            DriverJson.Context.TunerSnapshot
        );

        Assert.NotNull(restored?.CurrentSession);
        Assert.Equal(SessionPurpose.Scan, restored.CurrentSession.Purpose);
        Assert.Equal(27, restored.CurrentSession.Tune?.IsdbT?.PhysicalChannel);
        Assert.Equal(Moment, restored.CurrentSession.StartedAt);
    }

    [Fact]
    public void AQualityReadingRidesOnTheTunerItWasTakenFrom()
    {
        var tuner = Idle with
        {
            SignalQuality = new SignalQualityDto
            {
                Lock = SignalLock.Locked,
                CnrMilliDecibels = 21_500,
                PostViterbiBitErrors = [new LayerBitErrorCounts(0, 12, 1_000_000)],
            },
        };

        var restored = DriverJson.Deserialize(
            DriverJson.Serialize(tuner),
            DriverJson.Context.TunerSnapshot
        );

        Assert.Equal(21_500, restored?.SignalQuality?.CnrMilliDecibels);
    }

    [Fact]
    public void AnUnlockedTunerOnTheWireCarriesNoMeasurement()
    {
        var json = DriverJson.Serialize(
            Idle with
            {
                SignalQuality = new SignalQualityDto
                {
                    Lock = SignalLock.NotLocked,
                    CnrMilliDecibels = 17,
                },
            }
        );

        var restored = DriverJson.Deserialize(json, DriverJson.Context.TunerSnapshot);

        Assert.Null(restored?.SignalQuality?.CnrMilliDecibels);
    }

    [Fact]
    public void ASessionMayBeOpenedForAScan()
    {
        var request = new StartSessionRequest
        {
            SessionId = SessionId.Parse("scan-1"),
            Purpose = SessionPurpose.Scan,
            Tuning = TuneParams.Terrestrial(27).ToLegacyRequest(),
            Tune = TuneParams.Terrestrial(27),
        };

        Assert.Empty(request.Validate(Moment));
        Assert.Contains("\"purpose\":\"scan\"", DriverJson.Serialize(request), StringComparison.Ordinal);
    }

    [Fact]
    public void ATypedTuneIsWhatIsCheckedWhenItIsPresent()
    {
        var request = new StartSessionRequest
        {
            SessionId = SessionId.Parse("scan-1"),
            Purpose = SessionPurpose.Scan,
            Tuning = TuneParams.Bs(15, 16625).ToLegacyRequest(),
            Tune = TuneParams.Bs(15, 16625),
        };

        Assert.Empty(request.Validate(Moment));
    }

    [Fact]
    public void ATypedTuneOutsideItsRangeIsRefusedByThePathItWasSentOn()
    {
        var request = new StartSessionRequest
        {
            SessionId = SessionId.Parse("scan-1"),
            Purpose = SessionPurpose.Scan,
            Tuning = new TuningRequest(TunerKind.Satellite, 7),
            Tune = TuneParams.Bs(7, 0),
        };

        Assert.Contains(
            request.Validate(Moment),
            problem => problem.StartsWith("tune.isdbSBs.bsChannel:", StringComparison.Ordinal)
        );
    }
}
