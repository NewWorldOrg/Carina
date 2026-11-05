using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public sealed partial class GlobalizationRuleTests
{
    [Fact]
    public void TheRepositoryAsksTheRuntimeForItsUnicodeTables()
        => Assert.Equal(
            "false",
            Declared(Path.Combine(RepositoryLayout.Root, "Directory.Build.props")));

    [Fact]
    public void NoProjectKeepsTheInvariantTablesBecauseEveryProcessHereFoldsText()
    {
        Assert.Empty(Projects()
            .Where(project => Declared(project) is not null)
            .Select(project => Path.GetRelativePath(RepositoryLayout.Root, project).Replace('\\', '/'))
            .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheRuleWouldSeeAProjectThatKeptTheInvariantTables()
    {
        string fixture = Path.Combine(
            RepositoryLayout.Root,
            "tests/Carina.Architecture.Tests/Fixtures/BreaksTheGlobalizationRule/Violating.csproj.fixture");

        Assert.Equal("true", Declared(fixture));
    }

    private static IEnumerable<string> Projects()
        => Directory
            .EnumerateFiles(RepositoryLayout.Root, "*.csproj", SearchOption.AllDirectories)
            .Where(project => !project.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !project.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string? Declared(string path)
        => Setting().Match(File.ReadAllText(path)) is { Success: true } found
            ? found.Groups[1].Value
            : null;

    [GeneratedRegex(@"<InvariantGlobalization>\s*([^<]+?)\s*</InvariantGlobalization>")]
    private static partial Regex Setting();
}
