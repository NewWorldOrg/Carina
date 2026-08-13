using Carina.Driver.Configuration;

namespace Carina.Driver.Tests;

/// <summary>
/// Everything the driver refuses to start with.
/// </summary>
/// <remarks>
/// The check runs before the socket is bound and before any device is opened, and
/// it reports every problem at once: a driver that fails on the first mistake makes
/// the operator restart it once per typo. Nothing here throws — a bad configuration
/// is an exit code and a message, not a stack trace from somewhere deeper.
/// </remarks>
public sealed class DriverConfigurationTests
{
    private const string Complete = """
        {
          "socketPath": "/run/carina/driver.sock",
          "recordingsDirectory": "/srv/recordings",
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
        var result = Read(Complete);

        Assert.Empty(result.Problems);
        Assert.NotNull(result.Configuration);
        Assert.Equal("/run/carina/driver.sock", result.Configuration.SocketPath);
        Assert.Equal(TunerBackend.Fake, result.Configuration.Tuner?.Backend);
        Assert.Single(result.Configuration.Devices!);
    }

    [Fact]
    public void EveryProblemIsReportedAtOnce()
    {
        var result = Read("""
            {
              "socketPath": "run/carina/driver.sock",
              "recordingsDirectory": "",
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
    [InlineData("recordingsDirectory", "\"srv/recordings\"")]
    [InlineData("shutdownGraceHours", "0")]
    [InlineData("shutdownGraceHours", "-1")]
    [InlineData("shutdownGraceHours", "169")]
    public void ASettingOutsideItsRangeNamesItself(string setting, string value)
    {
        var result = Read(Replace(setting, value));

        Assert.Contains(result.Problems, problem => problem.StartsWith($"{setting}:"));
    }

    // The message has to say which setting, what was expected and what was there,
    // because the operator reading it has only the message and the file.
    [Fact]
    public void AProblemCarriesTheExpectationAndTheValue()
    {
        var problem = Assert.Single(
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
        var result = Read("""{ "tuner": { "backend": "fake" } }""");

        Assert.Contains(result.Problems, problem => problem.StartsWith("socketPath:"));
        Assert.Contains(
            result.Problems,
            problem => problem.StartsWith("recordingsDirectory:")
        );
        Assert.Contains(result.Problems, problem => problem.StartsWith("devices:"));
    }

    [Fact]
    public void AnUnreadableFileIsAProblemAndNotAnException()
    {
        var result = Read("{ this is not json");

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
        var result = Read(
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
        var result = Read(Complete.Replace("\"adapter0\"", id));

        Assert.Contains(result.Problems, problem => problem.StartsWith("devices[0].id:"));
    }

    [Fact]
    public void ADeviceWithNoKindIsReported()
    {
        var result = Read(Complete.Replace("\"terrestrial\"", "\"quantum\""));

        Assert.Contains(
            result.Problems,
            problem => problem.StartsWith("devices[0].kind:")
        );
    }

    // The synthetic backend has no device nodes to name, so requiring one would make
    // the configuration that CI uses impossible to write.
    [Fact]
    public void TheSyntheticBackendDoesNotNeedADevicePath()
    {
        var result = Read(
            Complete.Replace("\"devicePath\": \"/dev/dvb/adapter0/frontend0\",", "")
        );

        Assert.Empty(result.Problems);
    }

    [Fact]
    public void TheHardwareBackendNeedsADevicePath()
    {
        var result = Read(
            Complete
                .Replace("\"backend\": \"fake\"", "\"backend\": \"dvb\"")
                .Replace("\"devicePath\": \"/dev/dvb/adapter0/frontend0\",", "")
        );

        Assert.Contains(
            result.Problems,
            problem => problem.StartsWith("devices[0].devicePath:")
        );
    }

    // Nothing in the shape can ask the driver to listen on a port: the socket is the
    // only way in, and a setting that could open a second one would make that a
    // matter of configuration rather than of construction.
    [Fact]
    public void ThereIsNoSettingThatCouldOpenAPort()
    {
        var settings = typeof(DriverConfiguration)
            .GetProperties()
            .Select(property => property.Name.ToLowerInvariant());

        Assert.DoesNotContain(settings, name => name.Contains("port") || name.Contains("url"));
    }

    private static string Replace(string setting, string value)
    {
        var start = Complete.IndexOf($"\"{setting}\":", StringComparison.Ordinal);
        var end = Complete.IndexOf(',', start);

        return Complete[..start] + $"\"{setting}\": {value}" + Complete[end..];
    }
}
