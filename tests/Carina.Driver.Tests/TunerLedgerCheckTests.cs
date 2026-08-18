using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Tuning;

namespace Carina.Driver.Tests;

public sealed class TunerLedgerCheckTests
{
    [Fact]
    public void ASatelliteTunerTheLedgerCallsTerrestrialContradictsTheLedger()
    {
        TunerContradiction contradiction = Assert.Single(
            TunerLedgerCheck.Contradictions(
                [Declared("adapter0.frontend0", DeviceKind.Terrestrial)],
                [Receiving("adapter0.frontend0", DeviceKind.Satellite)]
            )
        );

        Assert.Equal("adapter0.frontend0", contradiction.DeviceId);
        Assert.Equal(DeviceKind.Terrestrial, contradiction.Declared);
        Assert.Equal([DeviceKind.Satellite], contradiction.Receives);
    }

    [Fact]
    public void ATerrestrialTunerTheLedgerCallsSatelliteContradictsTheLedger()
    {
        TunerContradiction contradiction = Assert.Single(
            TunerLedgerCheck.Contradictions(
                [Declared("adapter0.frontend0", DeviceKind.Satellite)],
                [Receiving("adapter0.frontend0", DeviceKind.Terrestrial)]
            )
        );

        Assert.Equal(DeviceKind.Satellite, contradiction.Declared);
        Assert.Equal([DeviceKind.Terrestrial], contradiction.Receives);
    }

    [Fact]
    public void ATunerThatReceivesWhatTheLedgerClaimsIsLeftAlone()
    {
        Assert.Empty(
            TunerLedgerCheck.Contradictions(
                [
                    Declared("adapter0.frontend0", DeviceKind.Terrestrial),
                    Declared("adapter1.frontend0", DeviceKind.Satellite),
                ],
                [
                    Receiving("adapter0.frontend0", DeviceKind.Terrestrial),
                    Receiving("adapter1.frontend0", DeviceKind.Satellite),
                ]
            )
        );
    }

    [Theory]
    [InlineData(DeviceKind.Terrestrial)]
    [InlineData(DeviceKind.Satellite)]
    public void ATunerThatReceivesBothIsContradictedByNeitherClaim(DeviceKind declared)
    {
        Assert.Empty(
            TunerLedgerCheck.Contradictions(
                [Declared("adapter0.frontend0", declared)],
                [
                    Receiving(
                        "adapter0.frontend0",
                        DeviceKind.Terrestrial,
                        DeviceKind.Satellite
                    ),
                ]
            )
        );
    }

    [Fact]
    public void ATunerAnotherProcessHoldsSaysNothingAboutTheLedgerAndIsNotContradicted()
    {
        Assert.Empty(
            TunerLedgerCheck.Contradictions(
                [Declared("adapter0.frontend0", DeviceKind.Terrestrial)],
                [
                    new TunerDetection(
                        "adapter0.frontend0",
                        [],
                        DeviceDetection.Busy,
                        "another process is already holding this tuner"
                    ),
                ]
            )
        );
    }

    [Fact]
    public void ATunerThatWouldNotSayWhatItReceivesIsNotContradicted()
    {
        Assert.Empty(
            TunerLedgerCheck.Contradictions(
                [Declared("adapter0.frontend0", DeviceKind.Terrestrial)],
                [
                    new TunerDetection(
                        "adapter0.frontend0",
                        [],
                        DeviceDetection.Unreadable,
                        "it enumerated no delivery systems at all"
                    ),
                ]
            )
        );
    }

    [Fact]
    public void ATunerTheLedgerNamesAndTheProbeNeverSawIsNotContradicted()
    {
        Assert.Empty(
            TunerLedgerCheck.Contradictions(
                [Declared("adapter0.frontend0", DeviceKind.Terrestrial)],
                []
            )
        );
    }

    [Fact]
    public void ATunerTheProbeFoundAndTheLedgerDoesNotNameIsNotContradicted()
    {
        Assert.Empty(
            TunerLedgerCheck.Contradictions(
                [Declared("adapter0.frontend0", DeviceKind.Terrestrial)],
                [
                    Receiving("adapter0.frontend0", DeviceKind.Terrestrial),
                    Receiving("adapter9.frontend0", DeviceKind.Satellite),
                ]
            )
        );
    }

    [Fact]
    public void ATunerTurnedOffInTheLedgerIsStillCheckedAgainstWhatItReceives()
    {
        Assert.Single(
            TunerLedgerCheck.Contradictions(
                [Declared("adapter0.frontend0", DeviceKind.Terrestrial, Enabled: false)],
                [Receiving("adapter0.frontend0", DeviceKind.Satellite)]
            )
        );
    }

    [Fact]
    public void EveryContradictingTunerIsReportedRatherThanTheFirstOneFound()
    {
        Assert.Equal(
            ["adapter0.frontend0", "adapter1.frontend0"],
            TunerLedgerCheck
                .Contradictions(
                    [
                        Declared("adapter0.frontend0", DeviceKind.Terrestrial),
                        Declared("adapter1.frontend0", DeviceKind.Satellite),
                    ],
                    [
                        Receiving("adapter0.frontend0", DeviceKind.Satellite),
                        Receiving("adapter1.frontend0", DeviceKind.Terrestrial),
                    ]
                )
                .Select(contradiction => contradiction.DeviceId)
        );
    }

    [Fact]
    public void ALedgerThatNamesNoDeviceContradictsNothing()
    {
        Assert.Empty(
            TunerLedgerCheck.Contradictions(
                null,
                [Receiving("adapter0.frontend0", DeviceKind.Satellite)]
            )
        );
    }

    [Fact]
    public void TheContradictionSaysBothWhatTheLedgerClaimsAndWhatTheTunerReports()
    {
        TunerContradiction contradiction = Assert.Single(
            TunerLedgerCheck.Contradictions(
                [Declared("adapter0.frontend0", DeviceKind.Terrestrial)],
                [Receiving("adapter0.frontend0", DeviceKind.Satellite)]
            )
        );

        Assert.Contains("adapter0.frontend0", contradiction.Detail, StringComparison.Ordinal);
        Assert.Contains("terrestrial", contradiction.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("satellite", contradiction.Detail, StringComparison.OrdinalIgnoreCase);
    }

    private static DeviceSettings Declared(string id, DeviceKind kind, bool Enabled = true) =>
        new(id, kind, Enabled: Enabled);

    private static TunerDetection Receiving(string id, params DeviceKind[] receives) =>
        new(id, receives, DeviceDetection.Detected, null);
}
