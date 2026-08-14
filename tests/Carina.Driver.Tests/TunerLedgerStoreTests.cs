using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Tuning;

namespace Carina.Driver.Tests;

public sealed class TunerLedgerStoreTests : IDisposable
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

    private readonly string root = Directory.CreateTempSubdirectory("carina-store-").FullName;
    private readonly string path;
    private readonly DriverConfiguration configuration;

    public TunerLedgerStoreTests()
    {
        path = Path.Combine(root, "driver.json");
        configuration = new DriverConfiguration(
            "/run/carina/driver.sock",
            [new OutputRootSettings("primary", "/srv/recordings")],
            6,
            new TunerSettings(TunerBackend.Dvb, 30, 8 * 1024 * 1024),
            [
                new DeviceSettings(
                    "adapter0.frontend0",
                    DeviceKind.Terrestrial,
                    "/dev/dvb/adapter0/frontend0"
                ),
            ]
        );

        File.WriteAllText(path, DriverConfigurationWriter.Serialize(configuration));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private TunerLedgerStore Store() => new(configuration, path);

    [Fact]
    public void WhatWasSavedAndWhatIsRunningAgreeWhenNobodyHasTouchedTheFile()
    {
        var view = Store().View();

        Assert.Equal(view.LoadedHash, view.SavedHash);
        Assert.False(view.HasDrifted());
    }

    [Fact]
    public void TheLedgerReadsOutTheTunersTheDriverIsRunningWith()
    {
        Assert.Equal(
            ["adapter0.frontend0"],
            Store().View().Tuners.Select(entry => entry.DeviceId)
        );
    }

    [Fact]
    public void AnOperatorEditingTheFileUnderARunningDriverShowsUpAsDrift()
    {
        File.WriteAllText(
            path,
            DriverConfigurationWriter.Serialize(
                configuration with
                {
                    Devices =
                    [
                        new DeviceSettings(
                            "adapter0.frontend0",
                            DeviceKind.Terrestrial,
                            "/dev/dvb/adapter0/frontend0",
                            Enabled: false
                        ),
                        new DeviceSettings(
                            "adapter1.frontend0",
                            DeviceKind.Satellite,
                            "/dev/dvb/adapter1/frontend0"
                        ),
                    ],
                }
            )
        );

        var view = Store().View();

        Assert.True(view.HasDrifted());
        Assert.NotEqual(view.LoadedHash, view.SavedHash);
    }

    [Fact]
    public void ALedgerOnDiskThatNoLongerParsesIsDriftRatherThanAgreement()
    {
        File.WriteAllText(path, "{ this is not the configuration }");

        var view = Store().View();

        Assert.Null(view.SavedHash);
        Assert.True(view.HasDrifted());
    }

    [Fact]
    public void ASavedLedgerIsWhatTheNextStartWillLoadWhileTheRunningOneIsUnchanged()
    {
        var store = Store();
        var loaded = store.View().LoadedHash;

        var saved = store.Save(
            [
                new TunerConfigEntry { DeviceId = "adapter0.frontend0" },
                new TunerConfigEntry { DeviceId = "adapter1.frontend0" },
            ],
            [Terrestrial, Satellite]
        );

        Assert.Equal(LedgerRefusal.None, saved.Refusal);

        var view = store.View();

        Assert.Equal(loaded, view.LoadedHash);
        Assert.True(view.HasDrifted());
        Assert.Equal(
            ["adapter0.frontend0", "adapter1.frontend0"],
            view.Tuners.Select(entry => entry.DeviceId)
        );
    }

    [Fact]
    public void ASavedLedgerIsOneTheDriverCanStartFromAgain()
    {
        Store()
            .Save(
                [
                    new TunerConfigEntry { DeviceId = "adapter1.frontend0", LnbPower = true },
                    new TunerConfigEntry { DeviceId = "adapter0.frontend0" },
                ],
                [Terrestrial, Satellite]
            );

        var reread = DriverConfigurationReader.ReadFile(path, checkTheFilesystem: false);

        Assert.True(reread.TryGetConfiguration(out var written, out var problems), string.Join(" ", problems));
        Assert.Equal(
            ["adapter1.frontend0", "adapter0.frontend0"],
            (written.Devices ?? []).Select(device => device.Id)
        );
    }

    [Fact]
    public void SavingKeepsEverythingInTheFileThatIsNotAboutTuners()
    {
        Store().Save([new TunerConfigEntry { DeviceId = "adapter0.frontend0" }], [Terrestrial]);

        var reread = DriverConfigurationReader.ReadFile(path, checkTheFilesystem: false);

        Assert.True(reread.TryGetConfiguration(out var written, out _));
        Assert.Equal(configuration.SocketPath, written.SocketPath);
        Assert.Equal(configuration.ShutdownGraceHours, written.ShutdownGraceHours);
        Assert.Equal(configuration.LiveSessionMinutes, written.LiveSessionMinutes);
        Assert.Equal(configuration.SocketGroupId, written.SocketGroupId);
        Assert.Equal(
            (configuration.OutputRoots ?? []).Select(rootSetting => rootSetting.Name),
            (written.OutputRoots ?? []).Select(rootSetting => rootSetting.Name)
        );
        Assert.Equal(
            configuration.Tuner?.SignalQualitySeconds,
            written.Tuner?.SignalQualitySeconds
        );
        Assert.Equal(configuration.Tuner?.DemuxBufferBytes, written.Tuner?.DemuxBufferBytes);
    }

    [Fact]
    public void ALedgerThatWouldNotStartTheDriverIsRefusedBeforeItReachesTheFile()
    {
        var before = File.ReadAllText(path);

        var saved = Store()
            .Save(
                [new TunerConfigEntry { DeviceId = "adapter0.frontend0", Disabled = true }],
                [Terrestrial]
            );

        Assert.Equal(LedgerRefusal.Malformed, saved.Refusal);
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public void PuttingPowerOnTheCableOfATunerThatIsNotSatelliteIsRefusedBeforeItReachesTheFile()
    {
        var before = File.ReadAllText(path);

        var saved = Store()
            .Save(
                [new TunerConfigEntry { DeviceId = "adapter0.frontend0", LnbPower = true }],
                [Terrestrial]
            );

        Assert.Equal(LedgerRefusal.Malformed, saved.Refusal);
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public void AReaderHoldingTheLedgerOpenAcrossASaveReadsTheWholeOldOneRatherThanAMixture()
    {
        var before = File.ReadAllText(path);

        using var reader = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );

        Store()
            .Save(
                [
                    new TunerConfigEntry { DeviceId = "adapter0.frontend0" },
                    new TunerConfigEntry { DeviceId = "adapter1.frontend0" },
                ],
                [Terrestrial, Satellite]
            );

        using var held = new StreamReader(reader);

        Assert.Equal(before, held.ReadToEnd());
        Assert.NotEqual(before, File.ReadAllText(path));
    }

    [Fact]
    public void ASaveLeavesNothingBesideTheLedgerForTheNextStartToTripOver()
    {
        Store().Save([new TunerConfigEntry { DeviceId = "adapter0.frontend0" }], [Terrestrial]);

        Assert.Equal([path], Directory.EnumerateFileSystemEntries(root));
    }

    [Fact]
    public void ARefusedSaveLeavesTheLedgerOnDiskExactlyAsItWas()
    {
        var before = File.ReadAllBytes(path);

        Assert.Equal(LedgerRefusal.Empty, Store().Save([], [Terrestrial]).Refusal);
        Assert.Equal(
            LedgerRefusal.UnknownDevice,
            Store()
                .Save([new TunerConfigEntry { DeviceId = "adapter7.frontend0" }], [Terrestrial])
                .Refusal
        );

        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void ADriverToldNothingAboutWhereItsLedgerLivesRefusesToSaveRatherThanChooseAPlace()
    {
        var store = new TunerLedgerStore(configuration, null);

        var saved = store.Save(
            [new TunerConfigEntry { DeviceId = "adapter0.frontend0" }],
            [Terrestrial]
        );

        Assert.Equal(LedgerRefusal.Unwritable, saved.Refusal);
        Assert.Null(store.View().SavedHash);
    }

    [Fact]
    public void ARefusalNamesNoDeviceNode()
    {
        var saved = Store()
            .Save([new TunerConfigEntry { DeviceId = "adapter7.frontend0" }], [Terrestrial]);

        Assert.DoesNotContain("/dev", saved.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ARefusalAboutTheNodeDetectionOfferedNamesTheTunerAndNotTheNode()
    {
        var saved = Store()
            .Save(
                [new TunerConfigEntry { DeviceId = "adapter0.frontend0" }],
                [Terrestrial with { DevicePath = "/tmp/pretending/to/be/a/tuner" }]
            );

        Assert.Equal(LedgerRefusal.Malformed, saved.Refusal);
        Assert.Contains("adapter0.frontend0", saved.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("/tmp", saved.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSavedFileStaysReadableToWhoeverHasToEditItByHand()
    {
        Store().Save([new TunerConfigEntry { DeviceId = "adapter0.frontend0" }], [Terrestrial]);

        Assert.Contains("\n", File.ReadAllText(path), StringComparison.Ordinal);
    }
}
