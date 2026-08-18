using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Tuning;

namespace Carina.Driver.Tests;

public sealed class TunerLedgerTests
{
    private static readonly TunerDetection Terrestrial = new(
        "adapter0.frontend0",
        [DeviceKind.Terrestrial],
        DeviceDetection.Detected,
        null,
        "/dev/dvb/adapter0/frontend0"
    );

    private static readonly TunerDetection Satellite = new(
        "adapter1.frontend0",
        [DeviceKind.Satellite],
        DeviceDetection.Detected,
        null,
        "/dev/dvb/adapter1/frontend0"
    );

    private static readonly DeviceSettings[] Running =
    [
        new("adapter0.frontend0", DeviceKind.Terrestrial, "/dev/dvb/adapter0/frontend0"),
    ];

    [Fact]
    public void TheLedgerReadsOutAsTheEntriesTheOperatorEdits()
    {
        IReadOnlyList<TunerConfigEntry> entries = TunerLedger.Entries(
            [
                new("adapter0.frontend0", DeviceKind.Terrestrial, "/dev/dvb/adapter0/frontend0"),
                new(
                    "adapter1.frontend0",
                    DeviceKind.Satellite,
                    "/dev/dvb/adapter1/frontend0",
                    Enabled: false,
                    LnbPower: true
                ),
            ]
        );

        Assert.Equal(["adapter0.frontend0", "adapter1.frontend0"], entries.Select(e => e.DeviceId));
        Assert.False(entries[0].Disabled);
        Assert.True(entries[1].Disabled);
        Assert.True(entries[1].LnbPower);
    }

    [Fact]
    public void TheEntriesTheOperatorEditsCarryNoDevicePath()
    {
        string json = DriverJson.Serialize<IReadOnlyList<TunerConfigEntry>>(
            TunerLedger.Entries(Running)
        );

        Assert.DoesNotContain("/dev", json, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameLedgerFingerprintsTheSameWayWhicheverOrderItIsWrittenIn()
    {
        Assert.Equal(
            TunerLedger.Fingerprint(
                [
                    new("adapter0.frontend0", DeviceKind.Terrestrial, "/dev/dvb/adapter0/frontend0"),
                    new("adapter1.frontend0", DeviceKind.Satellite, "/dev/dvb/adapter1/frontend0"),
                ]
            ),
            TunerLedger.Fingerprint(
                [
                    new("adapter1.frontend0", DeviceKind.Satellite, "/dev/dvb/adapter1/frontend0"),
                    new("adapter0.frontend0", DeviceKind.Terrestrial, "/dev/dvb/adapter0/frontend0"),
                ]
            )
        );
    }

    [Theory]
    [InlineData(DeviceKind.Satellite, "/dev/dvb/adapter0/frontend0", true, false)]
    [InlineData(DeviceKind.Terrestrial, "/dev/dvb/adapter9/frontend0", true, false)]
    [InlineData(DeviceKind.Terrestrial, "/dev/dvb/adapter0/frontend0", false, false)]
    [InlineData(DeviceKind.Terrestrial, "/dev/dvb/adapter0/frontend0", true, true)]
    public void EveryPartOfATunerTheDriverActsOnChangesTheFingerprint(
        DeviceKind kind,
        string devicePath,
        bool enabled,
        bool lnbPower
    )
    {
        Assert.NotEqual(
            TunerLedger.Fingerprint(Running),
            TunerLedger.Fingerprint(
                [new("adapter0.frontend0", kind, devicePath, enabled, lnbPower)]
            )
        );
    }

    [Fact]
    public void AFingerprintIsHexadecimalAndTellsNobodyWhereTheDeviceNodeIs()
    {
        string fingerprint = TunerLedger.Fingerprint(Running);

        Assert.Equal(64, fingerprint.Length);
        Assert.All(fingerprint, c => Assert.True(char.IsAsciiDigit(c) || c is >= 'a' and <= 'f'));
        Assert.DoesNotContain("dvb", fingerprint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ALedgerWithNoTunersInItIsRefusedRatherThanSavedAsAnEmptyMachine()
    {
        LedgerRevision revision = TunerLedger.Revise([], [Terrestrial], Running);

        Assert.False(revision.TryGetDevices(out _));
        Assert.Equal(LedgerRefusal.Empty, revision.Refusal);
    }

    [Fact]
    public void AnEmptyLedgerSaysToUseTheSeparateOperationRatherThanThatItIsNotAllowed()
    {
        LedgerRevision revision = TunerLedger.Revise([], [Terrestrial], Running);

        Assert.Contains("detect", revision.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnEntryNamingSomethingThatWasNeverDetectedIsRefusedAndSaysWhich()
    {
        LedgerRevision revision = TunerLedger.Revise(
            [new TunerConfigEntry { DeviceId = "adapter7.frontend0" }],
            [Terrestrial],
            Running
        );

        Assert.False(revision.TryGetDevices(out _));
        Assert.Equal(LedgerRefusal.UnknownDevice, revision.Refusal);
        Assert.Contains("adapter7.frontend0", revision.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusingAnEntryNamesNoDeviceNode()
    {
        LedgerRevision revision = TunerLedger.Revise(
            [new TunerConfigEntry { DeviceId = "adapter7.frontend0" }],
            [Terrestrial],
            Running
        );

        Assert.DoesNotContain("/dev", revision.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEntryWhoseIdIsNotEvenAnIdentifierIsRefusedBeforeItIsLookedFor()
    {
        LedgerRevision revision = TunerLedger.Revise(
            [new TunerConfigEntry { DeviceId = "/dev/dvb/adapter0/frontend0" }],
            [Terrestrial],
            Running
        );

        Assert.False(revision.TryGetDevices(out _));
        Assert.Equal(LedgerRefusal.Malformed, revision.Refusal);
    }

    [Fact]
    public void TheSameTunerNamedTwiceIsRefusedRatherThanSavedTwice()
    {
        LedgerRevision revision = TunerLedger.Revise(
            [
                new TunerConfigEntry { DeviceId = "adapter0.frontend0" },
                new TunerConfigEntry { DeviceId = "adapter0.frontend0", LnbPower = true },
            ],
            [Terrestrial],
            Running
        );

        Assert.False(revision.TryGetDevices(out _));
        Assert.Equal(LedgerRefusal.Malformed, revision.Refusal);
    }

    [Fact]
    public void ASavedTunerTakesItsKindFromDetectionRatherThanFromWhoeverAskedToSaveIt()
    {
        LedgerRevision revision = TunerLedger.Revise(
            [new TunerConfigEntry { DeviceId = "adapter1.frontend0" }],
            [Satellite],
            Running
        );

        Assert.True(revision.TryGetDevices(out IReadOnlyList<DeviceSettings>? devices));
        Assert.Equal(DeviceKind.Satellite, Assert.Single(devices).Kind);
    }

    [Fact]
    public void SavingRepairsALedgerWhoseKindNoLongerMatchesTheHardware()
    {
        LedgerRevision revision = TunerLedger.Revise(
            [new TunerConfigEntry { DeviceId = "adapter0.frontend0" }],
            [Terrestrial with { Receives = [DeviceKind.Satellite] }],
            Running
        );

        Assert.True(revision.TryGetDevices(out IReadOnlyList<DeviceSettings>? devices));
        Assert.Equal(DeviceKind.Satellite, Assert.Single(devices).Kind);
    }

    [Fact]
    public void ATunerThatCouldNotBeReadKeepsTheKindTheLedgerAlreadyHadForIt()
    {
        LedgerRevision revision = TunerLedger.Revise(
            [new TunerConfigEntry { DeviceId = "adapter0.frontend0" }],
            [Terrestrial with { Receives = [], Detection = DeviceDetection.Busy }],
            Running
        );

        Assert.True(revision.TryGetDevices(out IReadOnlyList<DeviceSettings>? devices));
        Assert.Equal(DeviceKind.Terrestrial, Assert.Single(devices).Kind);
    }

    [Fact]
    public void ATunerNobodyCanReadAndNobodyHasSeenBeforeIsRefusedRatherThanGuessedAt()
    {
        LedgerRevision revision = TunerLedger.Revise(
            [new TunerConfigEntry { DeviceId = "adapter1.frontend0" }],
            [Satellite with { Receives = [], Detection = DeviceDetection.Busy }],
            Running
        );

        Assert.False(revision.TryGetDevices(out _));
        Assert.Equal(LedgerRefusal.UndeterminedKind, revision.Refusal);
    }

    [Fact]
    public void ANewlyDetectedTunerIsSavedWithTheNodeDetectionFoundItOn()
    {
        LedgerRevision revision = TunerLedger.Revise(
            [
                new TunerConfigEntry { DeviceId = "adapter0.frontend0" },
                new TunerConfigEntry { DeviceId = "adapter1.frontend0" },
            ],
            [Terrestrial, Satellite],
            Running
        );

        Assert.True(revision.TryGetDevices(out IReadOnlyList<DeviceSettings>? devices));
        Assert.Equal(
            ["/dev/dvb/adapter0/frontend0", "/dev/dvb/adapter1/frontend0"],
            devices.Select(device => device.DevicePath)
        );
    }

    [Fact]
    public void TurningATunerOffAndPuttingPowerOnTheCableAreTheOperatorsToSet()
    {
        LedgerRevision revision = TunerLedger.Revise(
            [
                new TunerConfigEntry
                {
                    DeviceId = "adapter1.frontend0",
                    Disabled = true,
                    LnbPower = true,
                },
                new TunerConfigEntry { DeviceId = "adapter0.frontend0" },
            ],
            [Terrestrial, Satellite],
            Running
        );

        Assert.True(revision.TryGetDevices(out IReadOnlyList<DeviceSettings>? devices));

        DeviceSettings satellite = Assert.Single(devices, device => device.Id is "adapter1.frontend0");

        Assert.False(satellite.Enabled);
        Assert.True(satellite.LnbPower);
    }

    [Fact]
    public void ATunerLeftOutOfTheSavedLedgerIsGoneFromIt()
    {
        LedgerRevision revision = TunerLedger.Revise(
            [new TunerConfigEntry { DeviceId = "adapter1.frontend0" }],
            [Terrestrial, Satellite],
            Running
        );

        Assert.True(revision.TryGetDevices(out IReadOnlyList<DeviceSettings>? devices));
        Assert.Equal("adapter1.frontend0", Assert.Single(devices).Id);
    }
}
