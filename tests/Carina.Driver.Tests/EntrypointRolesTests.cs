using System.Text.RegularExpressions;

namespace Carina.Driver.Tests;

public sealed partial class EntrypointRolesTests
{
    [Fact]
    public void TheRolesTheEntrypointTakesAreTheOnesTheReadmeLists()
    {
        string[] taken = Taken();
        string[] listed = Listed();

        Assert.NotEmpty(taken);
        Assert.Equal(listed.Order(StringComparer.Ordinal), taken.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheRoleTheEntrypointRefusesIsToldTheRolesItTakes()
    {
        string refusal = Array.Find(
            File.ReadAllLines(Entrypoint),
            line => line.Contains("unknown role", StringComparison.Ordinal)
        ) ?? string.Empty;

        string[] named = [.. Word().Matches(refusal[refusal.IndexOf("expected", StringComparison.Ordinal)..]).Select(match => match.Value)];

        Assert.Equal(
            Taken().Order(StringComparer.Ordinal),
            named.Except(["expected", "or"]).Order(StringComparer.Ordinal)
        );
    }

    private static string[] Taken()
    {
        string[] lines = File.ReadAllLines(Entrypoint);
        int opened = Array.FindIndex(lines, line => line.StartsWith("main()", StringComparison.Ordinal));

        Assert.True(opened >= 0, "docker/entrypoint.sh no longer has a main function.");

        return
        [
            .. lines
                .Skip(opened)
                .Select(line => Arm().Match(line))
                .Where(match => match.Success)
                .Select(match => match.Groups["role"].Value),
        ];
    }

    private static string[] Listed()
    {
        string[] lines = File.ReadAllLines(Path.Combine(RepositoryFiles.Root(), "README.md"));
        int opened = Array.FindIndex(lines, line => line == Heading);

        Assert.True(opened >= 0, $"README.md no longer has the section '{Heading}'.");

        return
        [
            .. lines
                .Skip(opened + 1)
                .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal))
                .Select(line => Row().Match(line))
                .Where(match => match.Success)
                .Select(match => match.Groups["role"].Value),
        ];
    }

    private static string Entrypoint => Path.Combine(RepositoryFiles.Root(), "docker", "entrypoint.sh");

    private const string Heading = "## イメージの役割";

    [GeneratedRegex(@"^\s+(?<role>[a-z]+)\)")]
    private static partial Regex Arm();

    [GeneratedRegex(@"^\| `(?<role>[a-z]+)` \|")]
    private static partial Regex Row();

    [GeneratedRegex("[a-z]+")]
    private static partial Regex Word();
}
