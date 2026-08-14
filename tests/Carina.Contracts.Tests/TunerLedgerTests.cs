namespace Carina.Contracts.Tests;

public sealed class TunerLedgerTests
{
    [Fact]
    public void AnEntryNamesADetectedDeviceAndNothingAboutTheMachine()
    {
        var json = DriverJson.Serialize(new TunerConfigEntry { DeviceId = "adapter0" });

        Assert.Equal(
            """{"deviceId":"adapter0","disabled":false,"lnbPower":false}""",
            json
        );
    }

    [Fact]
    public void AnEntryCannotCarryADevicePathOrAKindOfItsOwn()
    {
        var json = DriverJson.Serialize(
            new TunerConfigEntry { DeviceId = "adapter0", LnbPower = true }
        );

        Assert.DoesNotContain("path", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dvb", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("frontend", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("types", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("kind", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ATunerNobodyDisabledStaysInService()
    {
        var entry = DriverJson.Deserialize(
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
        var entry = DriverJson.Deserialize("{}", DriverJson.Context.TunerConfigEntry);

        Assert.NotNull(entry);
        Assert.NotEmpty(entry.Validate());
    }

    [Fact]
    public void TheLedgerIsABareArray()
    {
        Assert.Equal(
            """[{"deviceId":"adapter0","disabled":false,"lnbPower":false}]""",
            DriverJson.Serialize<IReadOnlyList<TunerConfigEntry>>(
                [new TunerConfigEntry { DeviceId = "adapter0" }]
            )
        );

        Assert.Equal("[]", DriverJson.Serialize<IReadOnlyList<TunerConfigEntry>>([]));
    }

    [Fact]
    public void ADetectedDeviceReportsWhatTheDriverAskedTheHardware()
    {
        var json = DriverJson.Serialize(
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
        var device = DriverJson.Deserialize(
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
        var device = DriverJson.Deserialize(
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

        var device = DriverJson.Deserialize(
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
}
