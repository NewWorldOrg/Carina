using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public sealed partial class GlobalizationRuleTests
{
    private static readonly string[] TheProcessesThatNormaliseNothing =
    [
        "src/Carina.Driver/Carina.Driver.csproj",
        "tests/Carina.Driver.Tests/Carina.Driver.Tests.csproj",
    ];

    [Fact]
    public void TheRepositoryAsksTheRuntimeForItsUnicodeTables()
        => Assert.Equal(
            "false",
            Declared(Path.Combine(RepositoryLayout.Root, "Directory.Build.props")));

    [Fact]
    public void OnlyTheProcessesThatNormaliseNothingKeepTheInvariantTablesAndTheirTestsGoWithThem()
    {
        string[] declaring =
        [
            .. Projects()
                .Where(project => Declared(project) is not null)
                .Select(project => Path.GetRelativePath(RepositoryLayout.Root, project).Replace('\\', '/'))
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(TheProcessesThatNormaliseNothing.Order(StringComparer.Ordinal), declaring);
        Assert.All(
            TheProcessesThatNormaliseNothing,
            project => Assert.Equal("true", Declared(Path.Combine(RepositoryLayout.Root, project))));
    }

    [Fact]
    public void TheRuleWouldSeeAProjectThatKeptTheInvariantTablesWithoutSayingSoHere()
    {
        string fixture = Path.Combine(
            RepositoryLayout.Root,
            "tests/Carina.Architecture.Tests/Fixtures/BreaksTheGlobalizationRule/Violating.csproj.fixture");

        Assert.Equal("true", Declared(fixture));
        Assert.DoesNotContain(
            Path.GetRelativePath(RepositoryLayout.Root, fixture).Replace('\\', '/'),
            TheProcessesThatNormaliseNothing);
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
