namespace Carina.Contracts.Tests;

public sealed class SessionRequestAgreementTests
{
    private static readonly DateTimeOffset Moment =
        new(2026, 8, 8, 21, 4, 0, TimeSpan.FromHours(9));

    private static StartSessionRequest Request(TuningRequest? tuning, TuneParams? tune) =>
        new()
        {
            SessionId = SessionId.Parse("scan-1"),
            Purpose = SessionPurpose.Scan,
            Tuning = tuning!,
            Tune = tune,
        };

    [Fact]
    public void TheOlderFieldIsCheckedEvenWhenTypedParametersAreThere()
    {
        var problems = Request(
            new TuningRequest(TunerKind.Terrestrial, 900, -5),
            TuneParams.Terrestrial(27)
        ).Validate(Moment);

        Assert.Contains(
            problems,
            problem => problem.StartsWith("tuning.physicalChannel:", StringComparison.Ordinal)
        );
        Assert.Contains(
            problems,
            problem => problem.StartsWith("tuning.serviceId:", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void TheTwoFieldsHaveToNameTheSameTune()
    {
        var problems = Request(
            new TuningRequest(TunerKind.Terrestrial, 42),
            TuneParams.Terrestrial(27)
        ).Validate(Moment);

        Assert.Contains(
            problems,
            problem => problem.StartsWith("tuning:", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void AKindThatDisagreesWithTheTypedParametersIsRefused()
    {
        var problems = Request(
            new TuningRequest(TunerKind.Satellite, 27),
            TuneParams.Terrestrial(27)
        ).Validate(Moment);

        Assert.Contains(
            problems,
            problem => problem.StartsWith("tuning:", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void TheTwoFieldsSayingTheSameThingIsAccepted()
    {
        var tune = TuneParams.Terrestrial(27);

        Assert.Empty(Request(tune.ToLegacyRequest(), tune).Validate(Moment));
    }

    [Theory]
    [InlineData(TuneSystem.IsdbSBs)]
    [InlineData(TuneSystem.IsdbSCs110)]
    public void ASatelliteTuneStaysUsableEvenThoughTheOlderFieldCannotNameIt(TuneSystem system)
    {
        var tune = system is TuneSystem.IsdbSBs ? TuneParams.Bs(15, 16625) : TuneParams.Cs110(24);

        Assert.Empty(Request(tune.ToLegacyRequest(), tune).Validate(Moment));
    }

    [Fact]
    public void AMissingOlderFieldIsRefusedEvenWhenTypedParametersAreThere()
    {
        Assert.Equal(
            ["tuning: missing."],
            Request(null, TuneParams.Terrestrial(27)).Validate(Moment)
        );
    }

    [Fact]
    public void AMissingOlderFieldFromTheWireIsRefusedEvenWhenTypedParametersAreThere()
    {
        var request = DriverJson.Deserialize(
            """{"sessionId":"scan-1","purpose":"scan","tuning":null,"tune":{"system":"isdbT","isdbT":{"physicalChannel":27}}}""",
            DriverJson.Context.StartSessionRequest
        );

        Assert.NotNull(request);
        Assert.Equal(["tuning: missing."], request.Validate(Moment));
    }

    [Fact]
    public void TypedParametersThatAreWrongAreReportedWithoutBlamingTheOlderField()
    {
        var problems = Request(new TuningRequest(TunerKind.Terrestrial, 7), TuneParams.Bs(7, 0))
            .Validate(Moment);

        Assert.Contains(
            problems,
            problem => problem.StartsWith("tune.isdbSBs.bsChannel:", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            problems,
            problem => problem.StartsWith("tuning:", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void ARequestCarryingOnlyTheOlderFieldIsUnaffected()
    {
        Assert.Empty(
            Request(new TuningRequest(TunerKind.Terrestrial, 27, 1024), null).Validate(Moment)
        );
    }

    [Fact]
    public void AServiceIdMayStillRideAlongsideTypedParameters()
    {
        Assert.Empty(
            Request(new TuningRequest(TunerKind.Terrestrial, 27, 1024), TuneParams.Terrestrial(27))
                .Validate(Moment)
        );
    }
}
