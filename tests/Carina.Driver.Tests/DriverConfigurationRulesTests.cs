using Carina.Driver.Configuration;

namespace Carina.Driver.Tests;

public sealed class DriverConfigurationRulesTests
{
    private const string Complete = """
        {
          "socketPath": "/run/carina/driver.sock",
          "outputRoots": [{ "name": "primary", "path": "/srv/recordings" }],
          "shutdownGraceHours": 6,
          "tuner": { "backend": "fake" },
          "devices": [
            { "id": "adapter0", "kind": "terrestrial", "enabled": true }
          ]
        }
        """;

    private static IReadOnlyList<string> Problems(string json) =>
        DriverConfigurationReader.Read(json).Problems;

    private static string WithDevices(string devices) =>
        Complete.Replace(
            """
            "devices": [
                { "id": "adapter0", "kind": "terrestrial", "enabled": true }
              ]
            """.Trim(),
            devices
        );

    [Fact]
    public void ANullDeviceIsAFindingAndNotACrash()
    {
        Assert.Contains(
            Problems(WithDevices("""
                "devices": [ null ]
                """)),
            problem => problem.StartsWith("devices[0]:")
        );
    }

    [Theory]
    [InlineData("\"listenPort\": 9000,", "listenPort:")]
    [InlineData("\"socket_path\": \"/run/x.sock\",", "socket_path:")]
    public void AnUnknownSettingIsNamed(string extra, string expected)
    {
        var json = Complete.Replace("{\n  \"socketPath\"", "{\n  " + extra + "\n  \"socketPath\"");

        Assert.Contains(Problems(json), problem => problem.StartsWith(expected));
    }

    [Fact]
    public void AnUnknownDeviceSettingIsNamed()
    {
        Assert.Contains(
            Problems(WithDevices("""
                "devices": [
                    { "id": "adapter0", "kind": "terrestrial", "enbled": false }
                  ]
                """)),
            problem => problem.StartsWith("devices[0].enbled:")
        );
    }

    [Fact]
    public void AnUnknownTunerSettingIsNamed()
    {
        var json = Complete.Replace(
            "\"tuner\": { \"backend\": \"fake\" }",
            "\"tuner\": { \"backend\": \"fake\", \"bakcend\": \"dvb\" }"
        );

        Assert.Contains(Problems(json), problem => problem.StartsWith("tuner.bakcend:"));
    }

    [Fact]
    public void TwoDevicesOnOneNodeAreAFinding()
    {
        var json = WithDevices("""
            "devices": [
                { "id": "a0", "kind": "terrestrial", "devicePath": "/dev/dvb/adapter0/frontend0" },
                { "id": "a1", "kind": "terrestrial", "devicePath": "/dev/dvb/adapter0/frontend0" }
              ]
            """).Replace("\"backend\": \"fake\"", "\"backend\": \"dvb\"");

        Assert.Contains(
            Problems(json),
            problem => problem.StartsWith("devices[1].devicePath:")
        );
    }

    [Fact]
    public void PoweringALowNoiseBlockOnATerrestrialDeviceIsAFinding()
    {
        Assert.Contains(
            Problems(WithDevices("""
                "devices": [
                    { "id": "adapter0", "kind": "terrestrial", "lnbPower": true }
                  ]
                """)),
            problem => problem.StartsWith("devices[0].lnbPower:")
        );
    }

    [Fact]
    public void ASatelliteDeviceMayPowerItsLowNoiseBlock()
    {
        Assert.Empty(
            Problems(WithDevices("""
                "devices": [
                    { "id": "adapter0", "kind": "satellite", "lnbPower": true }
                  ]
                """))
        );
    }

    [Fact]
    public void DevicesThatAreAllDisabledAreAFinding()
    {
        Assert.Contains(
            Problems(WithDevices("""
                "devices": [
                    { "id": "adapter0", "kind": "terrestrial", "enabled": false }
                  ]
                """)),
            problem => problem.StartsWith("devices:")
        );
    }

    [Fact]
    public void ADevicePathIsCheckedEvenWhenTheBackendCannotBeRead()
    {
        var json = WithDevices("""
            "devices": [
                { "id": "adapter0", "kind": "terrestrial" }
              ]
            """).Replace("\"backend\": \"fake\"", "\"backend\": \"DVB\"");

        var problems = Problems(json);

        Assert.Contains(problems, problem => problem.StartsWith("tuner.backend:"));
        Assert.Contains(problems, problem => problem.StartsWith("devices[0].devicePath:"));
    }

    [Theory]
    [InlineData("/tmp/carina.sock")]
    [InlineData("/run")]
    public void ASocketOutsideTheRunDirectoryIsAFinding(string socketPath)
    {
        var json = Complete.Replace("/run/carina/driver.sock", socketPath);

        Assert.Contains(Problems(json), problem => problem.StartsWith("socketPath:"));
    }

    [Fact]
    public void ADevicePathOutsideDevIsAFinding()
    {
        var json = WithDevices("""
            "devices": [
                { "id": "adapter0", "kind": "terrestrial", "devicePath": "/etc/shadow" }
              ]
            """).Replace("\"backend\": \"fake\"", "\"backend\": \"dvb\"");

        Assert.Contains(
            Problems(json),
            problem => problem.StartsWith("devices[0].devicePath:")
        );
    }
}
