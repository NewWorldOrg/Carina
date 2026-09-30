using System.Globalization;

using Carina.Driver.Configuration;

namespace Carina.Driver.Tests;

public sealed class EntrypointAppUserTests
{
    [Fact]
    public void AnAppStartedAsRootDropsIntoTheGroupTheDriverGivesItsSocketByDefault()
    {
        string[] lines = File.ReadAllLines(Path.Combine(RepositoryFiles.Root(), "docker", "entrypoint.sh"));
        string? line = Array.Find(lines, candidate => candidate.StartsWith(Declaration, StringComparison.Ordinal));

        Assert.True(line is not null, $"docker/entrypoint.sh no longer declares '{Declaration}'.");
        Assert.Equal(
            DriverConfiguration.DefaultSocketGroupId.ToString(CultureInfo.InvariantCulture),
            line[Declaration.Length..]
        );
    }

    private const string Declaration = "readonly carina_gid=";
}
