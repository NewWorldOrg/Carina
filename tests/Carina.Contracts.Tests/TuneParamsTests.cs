namespace Carina.Contracts.Tests;

public sealed class TuneParamsTests
{
    [Fact]
    public void ATerrestrialTuneNamesItsPhysicalChannel()
    {
        var tune = TuneParams.Terrestrial(55);

        Assert.Equal(TuneSystem.IsdbT, tune.System);
        Assert.Equal(55, tune.IsdbT?.PhysicalChannel);
        Assert.Null(tune.IsdbSBs);
        Assert.Null(tune.IsdbSCs110);
        Assert.Empty(tune.Validate());
    }

    [Fact]
    public void ABsTuneCarriesTheStreamItExpects()
    {
        var tune = TuneParams.Bs(15, 50001);

        Assert.Equal(TuneSystem.IsdbSBs, tune.System);
        Assert.Equal(15, tune.IsdbSBs?.BsChannel);
        Assert.Equal(50001, tune.IsdbSBs?.Tsid);
        Assert.Empty(tune.Validate());
    }

    [Fact]
    public void ACs110TuneHasNoStreamToFilterOn()
    {
        var tune = TuneParams.Cs110(24);

        Assert.Equal(TuneSystem.IsdbSCs110, tune.System);
        Assert.Equal(24, tune.IsdbSCs110?.CsChannel);
        Assert.Empty(tune.Validate());
        Assert.DoesNotContain("tsid", DriverJson.Serialize(tune), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(13)]
    [InlineData(62)]
    public void TheTerrestrialRangeIsAccepted(int channel)
    {
        Assert.Empty(TuneParams.Terrestrial(channel).Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(63)]
    [InlineData(255)]
    public void APhysicalChannelOutsideTheTerrestrialRangeIsRefused(int channel)
    {
        Assert.Contains(
            TuneParams.Terrestrial(channel).Validate(),
            problem => problem.StartsWith("isdbT.physicalChannel:", StringComparison.Ordinal)
        );
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(23)]
    public void TheBsRangeIsAccepted(int channel)
    {
        Assert.Empty(TuneParams.Bs(channel, 0).Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(17)]
    [InlineData(25)]
    public void ABsSlotOutsideTheRangeIsRefused(int channel)
    {
        Assert.Contains(
            TuneParams.Bs(channel, 0).Validate(),
            problem => problem.StartsWith("isdbSBs.bsChannel:", StringComparison.Ordinal)
        );
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    public void AStreamIdentifierOutsideTheStandardIsRefused(int tsid)
    {
        Assert.Contains(
            TuneParams.Bs(15, tsid).Validate(),
            problem => problem.StartsWith("isdbSBs.tsid:", StringComparison.Ordinal)
        );
    }

    [Theory]
    [InlineData(2)]
    [InlineData(24)]
    public void TheCs110RangeIsAccepted(int channel)
    {
        Assert.Empty(TuneParams.Cs110(channel).Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(26)]
    public void ACs110SlotOutsideTheRangeIsRefused(int channel)
    {
        Assert.Contains(
            TuneParams.Cs110(channel).Validate(),
            problem => problem.StartsWith("isdbSCs110.csChannel:", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void ATuneWithoutASystemIsRefused()
    {
        Assert.Contains(
            new TuneParams().Validate(),
            problem => problem.StartsWith("system:", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void ATuneWhoseSystemHasNoParametersIsRefused()
    {
        var tune = new TuneParams { System = TuneSystem.IsdbSBs };

        Assert.Contains(
            tune.Validate(),
            problem => problem.StartsWith("isdbSBs:", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void OnlyTheArmTheSystemNamesMayBeFilled()
    {
        TuneParams tune = TuneParams.Terrestrial(55) with { IsdbSCs110 = new IsdbSCs110Params(24) };

        Assert.Contains(
            tune.Validate(),
            problem => problem.StartsWith("isdbSCs110:", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void EachSystemKnowsWhichTunerCanServeIt()
    {
        Assert.Equal(TunerKind.Terrestrial, TuneParams.Terrestrial(55).Kind);
        Assert.Equal(TunerKind.Satellite, TuneParams.Bs(15, 0).Kind);
        Assert.Equal(TunerKind.Satellite, TuneParams.Cs110(24).Kind);
        Assert.Equal(TunerKind.Unspecified, new TuneParams().Kind);
    }

    [Fact]
    public void ATerrestrialTuneIsStillUnderstoodByADriverThatOnlyKnowsTheOlderShape()
    {
        TuningRequest legacy = TuneParams.Terrestrial(55).ToLegacyRequest();

        Assert.Equal(TunerKind.Terrestrial, legacy.Kind);
        Assert.Equal(55, legacy.PhysicalChannel);
        Assert.Null(legacy.ServiceId);
    }

    [Theory]
    [InlineData(TuneSystem.IsdbSBs)]
    [InlineData(TuneSystem.IsdbSCs110)]
    public void ASatelliteTuneTheOlderShapeCannotExpressIsSentAsSomethingItRefuses(
        TuneSystem system
    )
    {
        TuneParams tune = system is TuneSystem.IsdbSBs ? TuneParams.Bs(15, 50001) : TuneParams.Cs110(24);

        TuningRequest legacy = tune.ToLegacyRequest();

        Assert.Equal(TunerKind.Unspecified, legacy.Kind);

        var request = new StartSessionRequest
        {
            SessionId = SessionId.Parse("scan-1"),
            Purpose = SessionPurpose.Live,
            Tuning = legacy,
        };

        Assert.Contains(
            request.Validate(DateTimeOffset.UnixEpoch),
            problem => problem.StartsWith("tuning.kind:", StringComparison.Ordinal)
        );
    }
}
