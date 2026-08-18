using Carina.Driver.Configuration;

namespace Carina.Driver.Tests;

public sealed class DriverConfigurationTests
{
    private const string Complete = """
        {
          "socketPath": "/run/carina/driver.sock",
          "outputRoots": [{ "name": "primary", "path": "/srv/recordings" }],
          "shutdownGraceHours": 6,
          "tuner": { "backend": "fake" },
          "devices": [
            {
              "id": "adapter0",
              "kind": "terrestrial",
              "devicePath": "/dev/dvb/adapter0/frontend0",
              "enabled": true
            }
          ]
        }
        """;

    private static DriverConfigurationResult Read(string json) =>
        DriverConfigurationReader.Read(json);

    [Fact]
    public void ACompleteConfigurationIsAccepted()
    {
        DriverConfigurationResult result = Read(Complete);

        Assert.Empty(result.Problems);
        Assert.NotNull(result.Configuration);
        Assert.Equal("/run/carina/driver.sock", result.Configuration.SocketPath);
        Assert.Equal(TunerBackend.Fake, result.Configuration.Tuner?.Backend);
        Assert.Single(result.Configuration.Devices!);
    }

    [Fact]
    public void EveryProblemIsReportedAtOnce()
    {
        DriverConfigurationResult result = Read("""
            {
              "socketPath": "run/carina/driver.sock",
              "outputRoots": [],
              "shutdownGraceHours": 0,
              "tuner": { "backend": "telepathy" },
              "devices": []
            }
            """);

        Assert.Null(result.Configuration);
        Assert.Equal(5, result.Problems.Count);
        Assert.All(result.Problems, problem => Assert.Contains(":", problem));
    }

    [Theory]
    [InlineData("socketPath", "\"\"")]
    [InlineData("socketPath", "\"driver.sock\"")]
    [InlineData("shutdownGraceHours", "0")]
    [InlineData("shutdownGraceHours", "-1")]
    [InlineData("shutdownGraceHours", "169")]
    public void ASettingOutsideItsRangeNamesItself(string setting, string value)
    {
        DriverConfigurationResult result = Read(Replace(setting, value));

        Assert.Contains(result.Problems, problem => problem.StartsWith($"{setting}:"));
    }

    [Fact]
    public void AProblemCarriesTheExpectationAndTheValue()
    {
        string problem = Assert.Single(
            Read(Replace("shutdownGraceHours", "0")).Problems,
            p => p.StartsWith("shutdownGraceHours:")
        );

        Assert.Contains("1", problem);
        Assert.Contains("168", problem);
        Assert.Contains("0", problem);
    }

    [Fact]
    public void AMissingSettingIsReportedRatherThanDefaulted()
    {
        DriverConfigurationResult result = Read("""{ "tuner": { "backend": "fake" } }""");

        Assert.Contains(result.Problems, problem => problem.StartsWith("socketPath:"));
        Assert.Contains(result.Problems, problem => problem.StartsWith("outputRoots:"));
        Assert.Contains(result.Problems, problem => problem.StartsWith("devices:"));
    }

    [Fact]
    public void AnUnreadableFileIsAProblemAndNotAnException()
    {
        DriverConfigurationResult result = Read("{ this is not json");

        Assert.Null(result.Configuration);
        Assert.Contains(result.Problems, problem => problem.StartsWith("file:"));
    }

    [Fact]
    public void AnEmptyDocumentIsAProblemAndNotAnException()
    {
        Assert.Contains(Read("null").Problems, problem => problem.StartsWith("file:"));
    }

    [Fact]
    public void ABackendThisBuildDoesNotKnowIsReported()
    {
        Assert.Contains(
            Read(Complete.Replace("\"fake\"", "\"telepathy\"")).Problems,
            problem => problem.StartsWith("tuner.backend:")
        );
    }

    [Fact]
    public void TwoDevicesWithOneNameAreReported()
    {
        DriverConfigurationResult result = Read(
            Complete.Replace(
                "\"devices\": [",
                """
                "devices": [
                    {
                      "id": "adapter0",
                      "kind": "satellite",
                      "devicePath": "/dev/dvb/adapter1/frontend0",
                      "enabled": true
                    },
                """
            )
        );

        Assert.Contains(result.Problems, problem => problem.Contains("adapter0"));
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"../../etc/passwd\"")]
    [InlineData("\"adapter 0\"")]
    public void ADeviceNameOutsideTheShapeIsReported(string id)
    {
        DriverConfigurationResult result = Read(Complete.Replace("\"adapter0\"", id));

        Assert.Contains(result.Problems, problem => problem.StartsWith("devices[0].id:"));
    }

    [Fact]
    public void ADeviceWithNoKindIsReported()
    {
        DriverConfigurationResult result = Read(Complete.Replace("\"terrestrial\"", "\"quantum\""));

        Assert.Contains(
            result.Problems,
            problem => problem.StartsWith("devices[0].kind:")
        );
    }

    [Fact]
    public void TheSyntheticBackendDoesNotNeedADevicePath()
    {
        DriverConfigurationResult result = Read(
            Complete.Replace("\"devicePath\": \"/dev/dvb/adapter0/frontend0\",", "")
        );

        Assert.Empty(result.Problems);
    }

    [Fact]
    public void TheHardwareBackendNeedsADevicePath()
    {
        DriverConfigurationResult result = Read(
            Complete
                .Replace("\"backend\": \"fake\"", "\"backend\": \"dvb\"")
                .Replace("\"devicePath\": \"/dev/dvb/adapter0/frontend0\",", "")
        );

        Assert.Contains(
            result.Problems,
            problem => problem.StartsWith("devices[0].devicePath:")
        );
    }

    [Fact]
    public void ThereIsNoSettingThatCouldOpenAPort()
    {
        IEnumerable<string> settings = typeof(DriverConfiguration)
            .GetProperties()
            .Select(property => property.Name.ToLowerInvariant());

        Assert.DoesNotContain(settings, name => name.Contains("port") || name.Contains("url"));
    }

    private static string Replace(string setting, string value)
    {
        int start = Complete.IndexOf($"\"{setting}\":", StringComparison.Ordinal);
        int end = Complete.IndexOf(',', start);

        return Complete[..start] + $"\"{setting}\": {value}" + Complete[end..];
    }
}
