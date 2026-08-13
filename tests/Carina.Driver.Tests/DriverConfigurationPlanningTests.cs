using Carina.Driver.Configuration;

namespace Carina.Driver.Tests;

public sealed class DriverConfigurationPlanningTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("carina-planning-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string WriteConfiguration(string json)
    {
        var path = Path.Combine(root, "driver.json");
        File.WriteAllText(path, json);

        return path;
    }

    private const string PointingAtNothingThatExists = """
        {
          "socketPath": "/run/carina/driver.sock",
          "socketGroupId": 10001,
          "outputRoots": [{ "name": "primary", "path": "/srv/recordings" }],
          "shutdownGraceHours": 6,
          "tuner": { "backend": "fake" },
          "devices": [{ "id": "fake-terrestrial", "kind": "terrestrial" }]
        }
        """;

    [Fact]
    public void ReadingForTheRuntimeRefusesPathsThatDoNotExist()
    {
        var path = WriteConfiguration(PointingAtNothingThatExists);

        var result = DriverConfigurationReader.ReadFile(path);

        Assert.False(result.TryGetConfiguration(out _, out var problems));
        Assert.Contains(problems, problem => problem.Contains("does not exist", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadingForPlanningAnswersBeforeThosePathsExist()
    {
        var path = WriteConfiguration(PointingAtNothingThatExists);

        var result = DriverConfigurationReader.ReadFile(path, checkTheFilesystem: false);

        Assert.True(result.TryGetConfiguration(out var configuration, out _));
        Assert.Equal(21690, DriverShutdownBudget.From(configuration).TotalSeconds);
    }

    [Fact]
    public void ReadingForPlanningStillRefusesASettingItCannotUse()
    {
        var path = WriteConfiguration(
            PointingAtNothingThatExists.Replace(
                "\"shutdownGraceHours\": 6",
                "\"shutdownGraceHours\": 400",
                StringComparison.Ordinal
            )
        );

        var result = DriverConfigurationReader.ReadFile(path, checkTheFilesystem: false);

        Assert.False(result.TryGetConfiguration(out _, out var problems));
        Assert.Contains(
            problems,
            problem => problem.Contains("shutdownGraceHours", StringComparison.Ordinal)
        );
    }
}
