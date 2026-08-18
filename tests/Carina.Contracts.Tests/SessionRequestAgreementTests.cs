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
        IReadOnlyList<string> problems = Request(
            new TuningRequest(TunerKind.Terrestrial, 900, -5),
            TuneParams.Terrestrial(55)
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
        IReadOnlyList<string> problems = Request(
            new TuningRequest(TunerKind.Terrestrial, 42),
            TuneParams.Terrestrial(55)
        ).Validate(Moment);

        Assert.Contains(
            problems,
            problem => problem.StartsWith("tuning:", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void AKindThatDisagreesWithTheTypedParametersIsRefused()
    {
        IReadOnlyList<string> problems = Request(
            new TuningRequest(TunerKind.Satellite, 55),
            TuneParams.Terrestrial(55)
        ).Validate(Moment);

        Assert.Contains(
            problems,
            problem => problem.StartsWith("tuning:", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void TheTwoFieldsSayingTheSameThingIsAccepted()
    {
        var tune = TuneParams.Terrestrial(55);

        Assert.Empty(Request(tune.ToLegacyRequest(), tune).Validate(Moment));
    }

    [Theory]
    [InlineData(TuneSystem.IsdbSBs)]
    [InlineData(TuneSystem.IsdbSCs110)]
    public void ASatelliteTuneStaysUsableEvenThoughTheOlderFieldCannotNameIt(TuneSystem system)
    {
        TuneParams tune = system is TuneSystem.IsdbSBs ? TuneParams.Bs(15, 50001) : TuneParams.Cs110(24);

        Assert.Empty(Request(tune.ToLegacyRequest(), tune).Validate(Moment));
    }

    [Fact]
    public void AMissingOlderFieldIsRefusedEvenWhenTypedParametersAreThere()
    {
        Assert.Equal(
            ["tuning: missing."],
            Request(null, TuneParams.Terrestrial(55)).Validate(Moment)
        );
    }

    [Fact]
    public void AMissingOlderFieldFromTheWireIsRefusedEvenWhenTypedParametersAreThere()
    {
        StartSessionRequest? request = DriverJson.Deserialize(
            """{"sessionId":"scan-1","purpose":"scan","tuning":null,"tune":{"system":"isdbT","isdbT":{"physicalChannel":55}}}""",
            DriverJson.Context.StartSessionRequest
        );

        Assert.NotNull(request);
        Assert.Equal(["tuning: missing."], request.Validate(Moment));
    }

    [Fact]
    public void TypedParametersThatAreWrongAreReportedWithoutBlamingTheOlderField()
    {
        IReadOnlyList<string> problems = Request(new TuningRequest(TunerKind.Terrestrial, 7), TuneParams.Bs(7, 0))
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
            Request(new TuningRequest(TunerKind.Terrestrial, 55, 50001), null).Validate(Moment)
        );
    }

    [Fact]
    public void AServiceIdHasNoMeaningBesideTypedParametersAndIsRefused()
    {
        Assert.Contains(
            Request(new TuningRequest(TunerKind.Terrestrial, 55, 50001), TuneParams.Terrestrial(55))
                .Validate(Moment),
            problem => problem.StartsWith("tuning.serviceId:", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void AServiceIdOnItsOwnIsStillAcceptedTheWayItAlwaysWas()
    {
        Assert.Empty(
            Request(new TuningRequest(TunerKind.Terrestrial, 55, 50001), null).Validate(Moment)
        );
    }

    [Fact]
    public void TheReasonGivenForASatelliteTuneDoesNotPromiseTheOlderFieldWouldWork()
    {
        string problem = Assert.Single(
            Request(new TuningRequest(TunerKind.Satellite, 15), TuneParams.Bs(15, 50001))
                .Validate(Moment)
        );

        Assert.StartsWith("tuning:", problem, StringComparison.Ordinal);
        Assert.Contains("cannot name a tune on isdbSBs", problem, StringComparison.Ordinal);
        Assert.Contains("refuses instead of tuning", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("tunes the same way", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("expected kind unspecified", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReasonGivenForATerrestrialTuneIsThatBothFieldsTuneAlike()
    {
        string problem = Assert.Single(
            Request(new TuningRequest(TunerKind.Terrestrial, 42), TuneParams.Terrestrial(55))
                .Validate(Moment)
        );

        Assert.Contains("tunes the same way", problem, StringComparison.Ordinal);
        Assert.Contains("physical channel 55", problem, StringComparison.Ordinal);
    }
}
