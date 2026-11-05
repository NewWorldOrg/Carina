using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public sealed partial class EntrypointRuleTests
{
    private const string Heading = "### イメージの役割";

    private const string GroupDeclaration = "readonly carina_gid=";

    private const string TheGroupTheDriverGivesItsSocketByDefault = "10001";

    [Fact]
    public void TheRolesTheEntrypointTakesAreTheOnesTheReadmeLists()
    {
        string[] taken = Taken();
        string[] listed = Listed();

        Assert.NotEmpty(taken);
        Assert.Equal(listed.Order(StringComparer.Ordinal), taken.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheEntrypointStartsNoDriverBecauseTheDriverIsAnImageOfItsOwn()
    {
        Assert.Equal(["app", "migrate"], Taken().Order(StringComparer.Ordinal));
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

    [Fact]
    public void AnAppStartedAsRootDropsIntoTheGroupTheDriverGivesItsSocketByDefault()
    {
        string[] lines = File.ReadAllLines(Entrypoint);
        string? declared = Array.Find(lines, line => line.StartsWith(GroupDeclaration, StringComparison.Ordinal));

        Assert.True(declared is not null, $"docker/entrypoint.sh no longer declares '{GroupDeclaration}'.");
        Assert.Equal(TheGroupTheDriverGivesItsSocketByDefault, declared[GroupDeclaration.Length..]);
        Assert.Contains(
            $"groupadd --gid {TheGroupTheDriverGivesItsSocketByDefault} carina",
            File.ReadAllText(Path.Combine(RepositoryLayout.Root, "Dockerfile")),
            StringComparison.Ordinal);
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
        string[] lines = File.ReadAllLines(Path.Combine(RepositoryLayout.Root, "README.md"));
        int opened = Array.FindIndex(lines, line => line == Heading);

        Assert.True(opened >= 0, $"README.md no longer has the section '{Heading}'.");

        return
        [
            .. lines
                .Skip(opened + 1)
                .TakeWhile(line => !line.StartsWith("#", StringComparison.Ordinal))
                .Select(line => Row().Match(line))
                .Where(match => match.Success)
                .Select(match => match.Groups["role"].Value),
        ];
    }

    private static string Entrypoint => Path.Combine(RepositoryLayout.Root, "docker", "entrypoint.sh");

    [GeneratedRegex(@"^\s+(?<role>[a-z]+)\)")]
    private static partial Regex Arm();

    [GeneratedRegex(@"^\| `(?<role>[a-z]+)` \|")]
    private static partial Regex Row();

    [GeneratedRegex("[a-z]+")]
    private static partial Regex Word();
}
