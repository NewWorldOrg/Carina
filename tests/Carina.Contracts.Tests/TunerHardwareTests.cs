namespace Carina.Contracts.Tests;

public sealed class TunerHardwareTests
{
    [Theory]
    [InlineData(TunerBus.Unspecified, "unspecified")]
    [InlineData(TunerBus.Pci, "pci")]
    [InlineData(TunerBus.Usb, "usb")]
    [InlineData(TunerBus.Synthetic, "synthetic")]
    [InlineData(TunerBus.Lent, "lent")]
    public void TheBusIsSpelledThisWay(TunerBus value, string wire)
    {
        Assert.Equal($"\"{wire}\"", DriverJson.Serialize(value, DriverJson.Context.TunerBus));
        Assert.Equal(value, DriverJson.Deserialize($"\"{wire}\"", DriverJson.Context.TunerBus));
    }

    [Fact]
    public void ABusThisBuildDoesNotKnowReadsAsUnspecified()
    {
        Assert.Equal(
            TunerBus.Unspecified,
            DriverJson.Deserialize("\"thunderbolt\"", DriverJson.Context.TunerBus)
        );
    }

    [Fact]
    public void TheCapabilityIsSpelledThisWay()
    {
        Assert.Equal("deviceHardware", DriverCapabilities.DeviceHardware);
    }

    [Fact]
    public void ATunerFromADriverThatNamesNoHardwareStillReads()
    {
        TunerSnapshot? tuner = DriverJson.Deserialize(
            """{"deviceId":"adapter0.frontend0","kind":"terrestrial","state":"idle"}""",
            DriverJson.Context.TunerSnapshot
        );

        Assert.NotNull(tuner);
        Assert.Null(tuner.Hardware);
        Assert.Null(tuner.Machine);
    }

    [Fact]
    public void ADetectedDeviceFromADriverThatNamesNoHardwareStillReads()
    {
        DetectedDeviceDto? device = DriverJson.Deserialize(
            """{"deviceId":"usb1-2","detection":"detected","kinds":["terrestrial"]}""",
            DriverJson.Context.DetectedDeviceDto
        );

        Assert.NotNull(device);
        Assert.Null(device.Hardware);
        Assert.Null(device.Machine);
    }

    [Fact]
    public void TheHardwareOfATunerCrossesTheWireIntact()
    {
        var hardware = new TunerHardwareDto
        {
            Name = "Example ISDB-T frontend",
            KernelDriver = "example_dvb",
            Bus = TunerBus.Pci,
            DevicePath = "/dev/dvb/adapter0/frontend0",
        };

        TunerSnapshot? tuner = DriverJson.Deserialize(
            DriverJson.Serialize(
                new TunerSnapshot("adapter0.frontend0", TunerKind.Terrestrial, TunerState.Idle)
                {
                    Hardware = hardware,
                }
            ),
            DriverJson.Context.TunerSnapshot
        );

        Assert.Equal(hardware, tuner?.Hardware);
    }

    [Fact]
    public void TheHardwareKeepsExactlyTheseFields()
    {
        Assert.Equal(
            """{"name":null,"kernelDriver":null,"bus":"usb","devicePath":null,"usbPort":"1-2"}""",
            DriverJson.Serialize(new TunerHardwareDto { Bus = TunerBus.Usb, UsbPort = "1-2" })
        );
    }

    [Fact]
    public void ADetectedDeviceTakesTheNewFieldsAtTheEnd()
    {
        Assert.Equal(
            """{"deviceId":"usb1-2","detection":"detected","kinds":[],"detail":null,"machine":null,"hardware":null}""",
            DriverJson.Serialize(
                new DetectedDeviceDto { DeviceId = "usb1-2", Detection = DeviceDetection.Detected }
            )
        );
    }
}
