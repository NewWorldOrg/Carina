using Carina.Contracts;
using Carina.Driver.Sessions;

namespace Carina.Driver.Tests;

public sealed class TuningKeyTests
{
    private static StartSessionRequest Request(TuningRequest tuning, TuneParams? tune = null) =>
        new()
        {
            SessionId = SessionId.Parse("s-1"),
            Purpose = SessionPurpose.Live,
            Tuning = tuning,
            Tune = tune,
        };

    [Fact]
    public void TwoConsumersOfDifferentServicesOnOneChannelAskForTheSameTuning()
    {
        var one = TuningKey.Of(Request(new TuningRequest(TunerKind.Terrestrial, 55, 50001)));
        var other = TuningKey.Of(Request(new TuningRequest(TunerKind.Terrestrial, 55, 50002)));

        Assert.Equal(one, other);
    }

    [Fact]
    public void AnotherChannelIsAnotherTuning()
    {
        var one = TuningKey.Of(Request(new TuningRequest(TunerKind.Terrestrial, 55)));
        var other = TuningKey.Of(Request(new TuningRequest(TunerKind.Terrestrial, 57)));

        Assert.NotEqual(one, other);
    }

    [Fact]
    public void TheSameChannelOnTheOtherSideOfTheDialIsAnotherTuning()
    {
        var terrestrial = TuningKey.Of(Request(new TuningRequest(TunerKind.Terrestrial, 15)));
        var satellite = TuningKey.Of(TuneParams.Bs(15, 50001));

        Assert.NotEqual(terrestrial, satellite);
    }

    [Fact]
    public void TheTypedFormAndTheOlderFieldNameTheSameTuning()
    {
        var tune = TuneParams.Terrestrial(55);

        Assert.Equal(
            TuningKey.Of(Request(new TuningRequest(TunerKind.Terrestrial, 55))),
            TuningKey.Of(Request(tune.ToLegacyRequest(), tune))
        );
    }

    [Fact]
    public void TheTwoStreamsCarriedOnOneSatelliteChannelAreTwoTunings()
    {
        Assert.NotEqual(
            TuningKey.Of(TuneParams.Bs(15, 50001)),
            TuningKey.Of(TuneParams.Bs(15, 50002))
        );
    }

    [Fact]
    public void TheStreamIsNamedWhereOneChannelCarriesMoreThanOne()
    {
        Assert.Contains("50001", TuningKey.Of(TuneParams.Bs(15, 50001)).ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("stream", TuningKey.Of(TuneParams.Terrestrial(55)).ToString(), StringComparison.Ordinal);
    }
}
