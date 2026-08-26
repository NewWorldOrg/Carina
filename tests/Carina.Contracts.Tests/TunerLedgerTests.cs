namespace Carina.Contracts.Tests;

public sealed class TunerLedgerTests
{
    [Fact]
    public void AnEntryNamesADetectedDeviceAndNothingAboutTheMachine()
    {
        string json = DriverJson.Serialize(new TunerConfigEntry { DeviceId = "adapter0" });

        Assert.Equal(
            """{"deviceId":"adapter0","disabled":false,"lnbPower":false,"kind":"unspecified"}""",
            json
        );
    }

    [Fact]
    public void AnEntryCannotCarryADevicePathOfItsOwn()
    {
        string json = DriverJson.Serialize(
            new TunerConfigEntry { DeviceId = "adapter0", LnbPower = true }
        );

        Assert.DoesNotContain("path", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dvb", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("frontend", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("types", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnEntryFromADriverThatPredatesTheKindSaysItDoesNotKnow()
    {
        TunerConfigEntry? entry = DriverJson.Deserialize(
            """{"deviceId":"adapter0","disabled":false,"lnbPower":false}""",
            DriverJson.Context.TunerConfigEntry
        );

        Assert.NotNull(entry);
        Assert.Equal(TunerKind.Unspecified, entry.Kind);
    }

    [Theory]
    [InlineData(TunerKind.Terrestrial, "terrestrial")]
    [InlineData(TunerKind.Satellite, "satellite")]
    [InlineData(TunerKind.Unspecified, "unspecified")]
    public void TheKindTheDriverSavedComesBackTheWayItWentOut(TunerKind kind, string wire)
    {
        string json = DriverJson.Serialize(new TunerConfigEntry { DeviceId = "adapter0", Kind = kind });

        Assert.Contains($"\"kind\":\"{wire}\"", json, StringComparison.Ordinal);
        Assert.Equal(kind, DriverJson.Deserialize(json, DriverJson.Context.TunerConfigEntry)!.Kind);
    }

    [Fact]
    public void ATunerNobodyDisabledStaysInService()
    {
        TunerConfigEntry? entry = DriverJson.Deserialize(
            """{"deviceId":"adapter0"}""",
            DriverJson.Context.TunerConfigEntry
        );

        Assert.NotNull(entry);
        Assert.False(entry.Disabled);
        Assert.False(entry.LnbPower);
        Assert.Empty(entry.Validate());
    }

    [Fact]
    public void PowerIsNotPutOnTheCableUnlessItWasAskedFor()
    {
        Assert.False(new TunerConfigEntry { DeviceId = "adapter0" }.LnbPower);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/dev/dvb/adapter0/frontend0")]
    [InlineData("../adapter0")]
    [InlineData("adapter 0")]
    public void AnEntryThatNamesSomethingOtherThanADetectedIdIsRefused(string deviceId)
    {
        Assert.Contains(
            new TunerConfigEntry { DeviceId = deviceId }.Validate(),
            problem => problem.StartsWith("deviceId:", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void AnEntryWithoutAnyDeviceIdIsRefused()
    {
        TunerConfigEntry? entry = DriverJson.Deserialize("{}", DriverJson.Context.TunerConfigEntry);

        Assert.NotNull(entry);
        Assert.NotEmpty(entry.Validate());
    }

    [Fact]
    public void TheLedgerIsABareArray()
    {
        Assert.Equal(
            """[{"deviceId":"adapter0","disabled":false,"lnbPower":false,"kind":"unspecified"}]""",
            DriverJson.Serialize<IReadOnlyList<TunerConfigEntry>>(
                [new TunerConfigEntry { DeviceId = "adapter0" }]
            )
        );

        Assert.Equal("[]", DriverJson.Serialize<IReadOnlyList<TunerConfigEntry>>([]));
    }

    [Fact]
    public void ADetectedDeviceReportsWhatTheDriverAskedTheHardware()
    {
        string json = DriverJson.Serialize(
            new DetectedDeviceDto
            {
                DeviceId = "adapter0",
                Detection = DeviceDetection.Detected,
                Kinds = [TunerKind.Terrestrial, TunerKind.Satellite],
            }
        );

        Assert.Equal(
            """{"deviceId":"adapter0","detection":"detected","kinds":["terrestrial","satellite"],"detail":null}""",
            json
        );
    }

    [Fact]
    public void ADeviceInUseIsStillReportedWithTheReasonItCouldNotBeRead()
    {
        var device = new DetectedDeviceDto
        {
            DeviceId = "adapter1",
            Detection = DeviceDetection.Busy,
            Detail = "another process holds the frontend",
        };

        Assert.Empty(device.Kinds);
        Assert.Contains("frontend", DriverJson.Serialize(device), StringComparison.Ordinal);
    }

    [Fact]
    public void ADetectionOutcomeThisBuildDoesNotKnowIsNotReadAsASuccess()
    {
        DetectedDeviceDto? device = DriverJson.Deserialize(
            """{"deviceId":"adapter0","detection":"warmingUp","kinds":["terrestrial"]}""",
            DriverJson.Context.DetectedDeviceDto
        );

        Assert.NotNull(device);
        Assert.Equal(DeviceDetection.Unspecified, device.Detection);
        Assert.NotEqual(DeviceDetection.Detected, device.Detection);
    }

    [Fact]
    public void AKindThisBuildDoesNotKnowLeavesTheOtherKindsReadable()
    {
        DetectedDeviceDto? device = DriverJson.Deserialize(
            """{"deviceId":"adapter0","detection":"detected","kinds":["terrestrial","isdbSky"]}""",
            DriverJson.Context.DetectedDeviceDto
        );

        Assert.NotNull(device);
        Assert.Equal(
            new[] { TunerKind.Terrestrial, TunerKind.Unspecified },
            device.Kinds
        );
    }

    [Fact]
    public void KindsAreNeverNull()
    {
        Assert.Empty(new DetectedDeviceDto { DeviceId = "adapter0", Kinds = null! }.Kinds);

        DetectedDeviceDto? device = DriverJson.Deserialize(
            """{"deviceId":"adapter0","kinds":null}""",
            DriverJson.Context.DetectedDeviceDto
        );

        Assert.NotNull(device);
        Assert.Empty(device.Kinds);
    }

    [Fact]
    public void TheDetectedListIsABareArray()
    {
        Assert.Equal("[]", DriverJson.Serialize<IReadOnlyList<DetectedDeviceDto>>([]));
    }

    [Fact]
    public void ASavedLedgerAnswersWithWhatWasWrittenAndWhatIsRunning()
    {
        Assert.Equal(
            """{"tuners":[{"deviceId":"adapter0","disabled":false,"lnbPower":false,"kind":"unspecified"}],"loadedHash":"aaaa","savedHash":"bbbb"}""",
            DriverJson.Serialize(
                new TunerLedgerDto
                {
                    Tuners = [new TunerConfigEntry { DeviceId = "adapter0" }],
                    LoadedHash = "aaaa",
                    SavedHash = "bbbb",
                }
            )
        );
    }

    [Fact]
    public void TheLedgerOnDiskAndTheLedgerInMemoryAgreeingIsTheAbsenceOfDrift()
    {
        Assert.False(
            new TunerLedgerDto { LoadedHash = "aaaa", SavedHash = "aaaa" }.HasDrifted()
        );
    }

    [Fact]
    public void ASaveThatHasNotBeenLoadedYetIsDrift()
    {
        Assert.True(
            new TunerLedgerDto { LoadedHash = "aaaa", SavedHash = "bbbb" }.HasDrifted()
        );
    }

    [Fact]
    public void ALedgerOnDiskThatCannotBeReadCountsAsDriftRatherThanAsAgreement()
    {
        Assert.True(new TunerLedgerDto { LoadedHash = "aaaa", SavedHash = null }.HasDrifted());
    }

    [Fact]
    public void DriftIsAComparisonTheReaderMakesRatherThanAFieldOnTheWire()
    {
        string json = DriverJson.Serialize(
            new TunerLedgerDto { LoadedHash = "aaaa", SavedHash = "aaaa" }
        );

        Assert.DoesNotContain("drift", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ALedgerAnswerCarriesNoDevicePath()
    {
        string json = DriverJson.Serialize(
            new TunerLedgerDto
            {
                Tuners = [new TunerConfigEntry { DeviceId = "adapter0.frontend0" }],
                LoadedHash = "aaaa",
                SavedHash = "aaaa",
            }
        );

        Assert.DoesNotContain("/dev", json, StringComparison.Ordinal);
        Assert.DoesNotContain("devicePath", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ALedgerAnswerFromAnOlderDriverStillReads()
    {
        TunerLedgerDto? ledger = DriverJson.Deserialize(
            """{"tuners":[{"deviceId":"adapter0"}]}""",
            DriverJson.Context.TunerLedgerDto
        );

        Assert.NotNull(ledger);
        Assert.Equal("adapter0", Assert.Single(ledger.Tuners).DeviceId);
        Assert.Null(ledger.LoadedHash);
        Assert.Null(ledger.SavedHash);
    }

    [Fact]
    public void TunersAreNeverNull()
    {
        Assert.Empty(new TunerLedgerDto { Tuners = null! }.Tuners);

        TunerLedgerDto? ledger = DriverJson.Deserialize(
            """{"tuners":null}""",
            DriverJson.Context.TunerLedgerDto
        );

        Assert.NotNull(ledger);
        Assert.Empty(ledger.Tuners);
    }

    [Fact]
    public void ATogglePutsATunerBackInServiceOrTakesItOut()
    {
        Assert.Equal(
            """{"disabled":true}""",
            DriverJson.Serialize(new TunerToggleRequest { Disabled = true })
        );

        TunerToggleRequest? request = DriverJson.Deserialize(
            """{"disabled":false}""",
            DriverJson.Context.TunerToggleRequest
        );

        Assert.NotNull(request);
        Assert.False(request.Disabled);
        Assert.Empty(request.Validate());
    }

    [Fact]
    public void AToggleThatSaysNothingIsRefusedRatherThanReadAsPuttingATunerBackInService()
    {
        TunerToggleRequest? request = DriverJson.Deserialize("{}", DriverJson.Context.TunerToggleRequest);

        Assert.NotNull(request);
        Assert.Null(request.Disabled);
        Assert.Contains(
            request.Validate(),
            problem => problem.StartsWith("disabled:", StringComparison.Ordinal)
        );
    }
}
