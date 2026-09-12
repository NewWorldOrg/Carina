using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public sealed partial class VersionRuleTests
{
    [Fact]
    public void TheRepositoryDeclaresItsVersionAtTheRootAndNowhereElse()
    {
        Assert.Single(VersionsDeclaredIn(RootProps(RepositoryLayout.Root)));
        Assert.Empty(BuildFilesTakingAVersionOfTheirOwn(RepositoryLayout.Root));
    }

    [Fact]
    public void EveryPropsFileBelowTheRootKeepsTheRootDeclarationInReach()
    {
        Assert.Empty(PropsFilesPuttingTheRootOutOfReach(RepositoryLayout.Root));
    }

    [Fact]
    public void TheRuleReachesBothOfTheProcessesThisRepositoryBuilds()
    {
        Assert.Equal(
            ["src/Carina.Api/Carina.Api.csproj", "src/Carina.Driver/Carina.Driver.csproj"],
            BuildFiles(RepositoryLayout.Root)
                .Select(file => Relative(RepositoryLayout.Root, file))
                .Where(file => file.EndsWith("/Carina.Api.csproj", StringComparison.Ordinal)
                    || file.EndsWith("/Carina.Driver.csproj", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheRuleWouldSeeAProcessThatTookAVersionOfItsOwn()
    {
        DirectoryInfo held = Directory.CreateTempSubdirectory("carina-version-rule-");

        try
        {
            Laid(held, "Directory.Build.props", "<Project><PropertyGroup><Version>0.1.0</Version></PropertyGroup></Project>");
            Laid(held, "src/Carina.Api/Carina.Api.csproj", "<Project />");
            Laid(
                held,
                "src/Carina.Driver/Carina.Driver.csproj",
                "<Project><PropertyGroup><Version>9.9.9</Version></PropertyGroup></Project>");

            Assert.Equal(
                ["src/Carina.Driver/Carina.Driver.csproj"],
                BuildFilesTakingAVersionOfTheirOwn(held.FullName));
        }
        finally
        {
            held.Delete(recursive: true);
        }
    }

    [Fact]
    public void TheRuleWouldSeeAPropsFileThatPutTheRootDeclarationOutOfReach()
    {
        DirectoryInfo held = Directory.CreateTempSubdirectory("carina-version-reach-");

        try
        {
            Laid(held, "Directory.Build.props", "<Project><PropertyGroup><Version>0.1.0</Version></PropertyGroup></Project>");
            Laid(held, "tests/Directory.Build.props", "<Project />");
            Laid(
                held,
                "src/Directory.Build.props",
                """<Project><Import Project="$(MSBuildThisFileDirectory)../Directory.Build.props" /></Project>""");

            Assert.Equal(
                ["tests/Directory.Build.props"],
                PropsFilesPuttingTheRootOutOfReach(held.FullName));
        }
        finally
        {
            held.Delete(recursive: true);
        }
    }

    private static string RootProps(string root) => Path.Combine(root, "Directory.Build.props");

    private static IReadOnlyList<string> BuildFilesTakingAVersionOfTheirOwn(string root)
        =>
        [
            .. BuildFiles(root)
                .Where(file => !string.Equals(file, RootProps(root), StringComparison.Ordinal))
                .Where(file => VersionsDeclaredIn(file).Count > 0)
                .Select(file => Relative(root, file))
                .Order(StringComparer.Ordinal),
        ];

    private static IReadOnlyList<string> PropsFilesPuttingTheRootOutOfReach(string root)
        =>
        [
            .. BuildFiles(root)
                .Where(file => Path.GetFileName(file) == "Directory.Build.props")
                .Where(file => !string.Equals(file, RootProps(root), StringComparison.Ordinal))
                .Where(file => !Import().IsMatch(File.ReadAllText(file)))
                .Select(file => Relative(root, file))
                .Order(StringComparer.Ordinal),
        ];

    private static IEnumerable<string> BuildFiles(string root)
        => new[] { "*.csproj", "Directory.Build.props" }
            .SelectMany(pattern => Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
            .Where(file =>
                !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static IReadOnlyList<string> VersionsDeclaredIn(string file)
        => [.. Declaration().Matches(File.ReadAllText(file)).Select(found => found.Groups[1].Value)];

    private static string Relative(string root, string file)
        => Path.GetRelativePath(root, file).Replace('\\', '/');

    private static void Laid(DirectoryInfo held, string path, string content)
    {
        string full = Path.Combine(held.FullName, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [GeneratedRegex(@"<(?:Version|VersionPrefix|AssemblyVersion|FileVersion|InformationalVersion)>\s*([^<]+?)\s*</")]
    private static partial Regex Declaration();

    [GeneratedRegex("""<Import\s[^>]*Project\s*=\s*"[^"]*Directory\.Build\.props"|<Import\s[^>]*Project\s*=\s*'[^']*Directory\.Build\.props'""")]
    private static partial Regex Import();
}
