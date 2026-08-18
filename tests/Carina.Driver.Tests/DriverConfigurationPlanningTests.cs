using Carina.Driver.Configuration;

namespace Carina.Driver.Tests;

public sealed class DriverConfigurationPlanningTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("carina-planning-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string WriteConfiguration(string json)
    {
        string path = Path.Combine(root, "driver.json");
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
        string path = WriteConfiguration(PointingAtNothingThatExists);

        DriverConfigurationResult result = DriverConfigurationReader.ReadFile(path);

        Assert.False(result.TryGetConfiguration(out _, out IReadOnlyList<string>? problems));
        Assert.Contains(problems, problem => problem.Contains("does not exist", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadingForPlanningAnswersBeforeThosePathsExist()
    {
        string path = WriteConfiguration(PointingAtNothingThatExists);

        DriverConfigurationResult result = DriverConfigurationReader.ReadFile(path, checkTheFilesystem: false);

        Assert.True(result.TryGetConfiguration(out DriverConfiguration? configuration, out _));
        Assert.Equal(21690, DriverShutdownBudget.From(configuration).TotalSeconds);
    }

    [Fact]
    public void ReadingForPlanningStillRefusesASettingItCannotUse()
    {
        string path = WriteConfiguration(
            PointingAtNothingThatExists.Replace(
                "\"shutdownGraceHours\": 6",
                "\"shutdownGraceHours\": 400",
                StringComparison.Ordinal
            )
        );

        DriverConfigurationResult result = DriverConfigurationReader.ReadFile(path, checkTheFilesystem: false);

        Assert.False(result.TryGetConfiguration(out _, out IReadOnlyList<string>? problems));
        Assert.Contains(
            problems,
            problem => problem.Contains("shutdownGraceHours", StringComparison.Ordinal)
        );
    }
}
