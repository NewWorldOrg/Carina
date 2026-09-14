using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public sealed class OutputRootRuleTests
{
    private const string Sweeping = "Integrity__OutputRoots";

    private const string Held = "Encodings__OutputRoots";

    [Fact(DisplayName = "A-エンコード-024: the roots the sweep walks and the roots artefacts are written into share no name")]
    public void TheRootsTheSweepWalksAndTheRootsArtefactsAreWrittenIntoShareNoName()
    {
        Assert.Empty(NamedByBothSettings(File.ReadAllText(ComposeFile(RepositoryLayout.Root))));
    }

    [Fact]
    public void BothSettingsAreActuallyThereToBeCompared()
    {
        string written = File.ReadAllText(ComposeFile(RepositoryLayout.Root));

        Assert.Equal(["primary"], NamedBy(written, Sweeping));
        Assert.Equal(["encodes"], NamedBy(written, Held));
    }

    [Fact]
    public void TheRuleWouldSeeADeploymentThatPointedBothSettingsAtOneName()
    {
        Assert.Equal(
            ["primary"],
            NamedByBothSettings(
                $"      {Sweeping}: primary=/srv/recordings;bulk=/mnt/bulk\n"
                + $"      {Held}: primary=/srv/encodes\n"));
    }

    private static IReadOnlyList<string> NamedByBothSettings(string written)
        => [.. NamedBy(written, Sweeping).Intersect(NamedBy(written, Held), StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    private static IReadOnlyList<string> NamedBy(string written, string setting)
    {
        Match found = Setting(setting).Match(written);

        return found.Success
            ? [.. found.Groups["roots"].Value
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(entry => entry.Split('=')[0].Trim())]
            : [];
    }

    private static Regex Setting(string setting)
        => new($@"^\s*{Regex.Escape(setting)}:\s*(?<roots>\S+)\s*$", RegexOptions.Multiline, TimeSpan.FromSeconds(5));

    private static string ComposeFile(string root) => Path.Combine(root, "compose.yml");
}
