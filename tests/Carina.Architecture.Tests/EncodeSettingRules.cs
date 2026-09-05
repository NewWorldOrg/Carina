using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

/// <summary>
/// Reads the encode settings out of their source rather than out of the assembly: this project
/// deliberately references no production project, so the rule is a source scan like the others here.
/// </summary>
public static class EncodeSettingRules
{
    private static readonly string[] FreeText = ["string", "string?"];

    private static readonly Regex Declared = new(
        @"\b(?<kind>class|record|struct|interface)\s+(?:class\s+|struct\s+)?(?<name>\w+)",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex Positional = new(
        @"\brecord\s+(?:class\s+|struct\s+)?(?<name>\w+)\s*\((?<parameters>[^)]*)\)",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex Member = new(
        @"^[ \t]*public\s+(?!static\b|const\b|abstract\b|class\b|sealed\b|record\b|readonly\s+struct\b|enum\b|interface\b|struct\b)(?:required\s+|virtual\s+|override\s+|readonly\s+)*(?<kind>[^\s(){};=]+)\s+(?<name>\w+)\s*(?<how>\{[^}]*\}|=>|;|=)",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    private static readonly Regex Bitrate = new(
        @"\b(?:class|record|struct|interface|enum)\s+(?:class\s+|struct\s+)?(?<name>\w*(?:[Bb]itrate|[Kk]ilobit)\w*)",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    public static IReadOnlyList<string> WhatASettingKeeps(string root, string directory)
        => Reported(root, directory, kept: true);

    public static IReadOnlyList<string> WhatASettingWorksOut(string root, string directory)
        => Reported(root, directory, kept: false);

    public static IReadOnlyList<string> WhatIsNamedForABitrate(string directory)
        => Scanned(directory)
            .SelectMany(file => Bitrate.Matches(file.Source).Select(match => match.Groups["name"].Value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static bool IsFreeText(string entry) => FreeText.Contains(Kind(entry), StringComparer.Ordinal);

    public static string Named(string entry)
    {
        string[] parts = Split(entry);

        return parts[1][(parts[1].IndexOf('.', StringComparison.Ordinal) + 1)..];
    }

    public static string Kind(string entry) => Split(entry)[2];

    private static string[] Split(string entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        string[] parts = entry.Split(' ');

        return parts.Length is 3
            ? parts
            : throw new ArgumentException($"'{entry}' is not one of this rule's entries.", nameof(entry));
    }

    private static IReadOnlyList<string> Reported(string root, string directory, bool kept)
        => Scanned(directory)
            .SelectMany(file => Fields(file.Source)
                .Where(field => field.Kept == kept)
                .Select(field => $"{Relative(root, file.Path)} {field.Owner}.{field.Name} {field.Kind}"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<Field> Fields(string source)
    {
        (int At, string Name)[] owners =
        [
            .. Declared.Matches(source).Select(match => (match.Index, match.Groups["name"].Value)),
        ];

        foreach (Match match in Member.Matches(source))
        {
            string how = match.Groups["how"].Value;

            yield return new Field(
                Owning(owners, match.Index),
                match.Groups["name"].Value,
                match.Groups["kind"].Value,
                Kept: !how.StartsWith("=>", StringComparison.Ordinal)
                    && !how.Contains("=>", StringComparison.Ordinal));
        }

        foreach (Match match in Positional.Matches(source))
        {
            foreach (string parameter in match.Groups["parameters"].Value.Split(','))
            {
                string[] said = parameter.Split(
                    [' ', '\t', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                if (said.Length is 2)
                {
                    yield return new Field(match.Groups["name"].Value, said[1], said[0], Kept: true);
                }
            }
        }
    }

    private static string Owning((int At, string Name)[] owners, int at)
        => owners.LastOrDefault(owner => owner.At < at).Name ?? "?";

    private static string Relative(string root, string path)
        => "/" + Path.GetRelativePath(root, path).Replace('\\', '/');

    private static IEnumerable<SourceFile> Scanned(string directory)
        => Directory
            .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .Order(StringComparer.Ordinal)
            .Select(file => new SourceFile(file, File.ReadAllText(file)));

    private static bool IsBuildOutput(string path)
    {
        string[] segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Contains("obj", StringComparer.Ordinal) || segments.Contains("bin", StringComparer.Ordinal);
    }

    private readonly record struct SourceFile(string Path, string Source);

    private readonly record struct Field(string Owner, string Name, string Kind, bool Kept);
}
