using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class SupplyStandingTests
{
    private static readonly DateTime Noon = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Threshold Applied =
        QualityThresholdShapes.AsShipped(QualityThresholdKey.SupplySilence, Noon);

    [Fact(DisplayName = "BR-QD-008: a pass says one thing about each supply rather than one thing about all four")]
    public void APassSaysOneThingAboutEachSupply()
        => Assert.Throws<ArgumentException>(() => SupplyStanding.Of(
            Noon,
            Applied,
            tunersWereAsked: true,
            [
                new SupplySilenceStanding(SupplySilence.SignalSamples, 1, 0),
                new SupplySilenceStanding(SupplySilence.SignalSamples, 1, 1),
            ]));

    [Fact]
    public void APassIsReadAtAMomentInUniversalTime()
        => Assert.Throws<ArgumentException>(() => SupplyStanding.Of(
            new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Unspecified),
            Applied,
            tunersWereAsked: true,
            []));

    [Fact]
    public void WhatAPassReadIsKeptAsItWasRead()
    {
        SupplyStanding standing = SupplyStanding.Of(
            Noon,
            Applied,
            tunersWereAsked: false,
            [new SupplySilenceStanding(SupplySilence.GuideVisits, 7, 2)]);

        Assert.Equal(Noon, standing.At);
        Assert.False(standing.TunersWereAsked);
        Assert.Equal(300, standing.Applied.Current);
        Assert.Equal(2, Assert.Single(standing.Supplies).Quiet);
    }
}
