using Carina.Driver.Configuration;

namespace Carina.Driver.Tests;

public sealed class DriverConfigurationFileTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("carina-test-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string Write(string name, string content)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, content);

        return path;
    }

    private string Configuration(string recordings, string socketDirectory) =>
        $$"""
        {
          "socketPath": "{{socketDirectory}}/driver.sock",
          "outputRoots": [{ "name": "primary", "path": "{{recordings}}" }],
          "shutdownGraceHours": 6,
          "tuner": { "backend": "fake" },
          "devices": [
            { "id": "adapter0", "kind": "terrestrial", "enabled": true }
          ]
        }
        """;

    [Fact]
    public void NoPathAtAllIsAFinding()
    {
        Assert.Contains(
            DriverConfigurationReader.ReadFile(null).Problems,
            problem => problem.StartsWith("file:")
        );
    }

    [Fact]
    public void AMissingFileNamesThePath()
    {
        var path = Path.Combine(root, "absent.json");

        Assert.Contains(
            DriverConfigurationReader.ReadFile(path).Problems,
            problem => problem.Contains(path)
        );
    }

    [Fact]
    public void ADirectoryIsNotMistakenForAnUnreadableFile()
    {
        var problem = Assert.Single(DriverConfigurationReader.ReadFile(root).Problems);

        Assert.Contains("directory", problem);
    }

    [Fact]
    public void AnOutputRootThatIsNotThereIsAFinding()
    {
        var path = Write(
            "driver.json",
            Configuration(Path.Combine(root, "absent"), "/run/carina")
        );

        Assert.Contains(
            DriverConfigurationReader.ReadFile(path).Problems,
            problem => problem.StartsWith("outputRoots[0].path:")
        );
    }

    [Fact]
    public void ASocketDirectoryThatIsNotThereIsAFinding()
    {
        var recordings = Directory.CreateDirectory(Path.Combine(root, "recordings")).FullName;
        var path = Write("driver.json", Configuration(recordings, "/run/absent"));

        Assert.Contains(
            DriverConfigurationReader.ReadFile(path).Problems,
            problem => problem.StartsWith("socketPath:")
        );
    }

    [Fact]
    public void TheShippedDevelopmentConfigurationIsUsable()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "docker",
            "driver.development.json"
        );

        Assert.Empty(DriverConfigurationReader.Read(File.ReadAllText(path)).Problems);
    }
}
