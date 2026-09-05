using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

/// <summary>
/// Reads the encode feature for the two things BR-ED2-010 forbids it: walking a directory to find
/// what to remove, and removing anywhere but where the ledger is read. Like the other rules here it
/// reads source text, so it sees the ordinary spellings and no others.
/// </summary>
public static partial class EncodeScratchRules
{
    public const string FeatureFolder = "/Encodings/";

    public static IReadOnlyList<string> WhatWalksADirectory(string directory)
        => Reported(directory, Walks());

    public static IReadOnlyList<string> WhatDeletes(string directory)
        => Reported(directory, Deletes());

    public static IReadOnlyList<string> WhatWalksADirectoryIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return [.. Walks().Matches(source).Select(match => Squeezed(match.Value)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    public static IReadOnlyList<string> FilesInTheFeature(string directory)
        => Scanned(directory).Select(file => file.Relative).Order(StringComparer.Ordinal).ToArray();

    private static IReadOnlyList<string> Reported(string directory, Regex marks)
        => Scanned(directory)
            .SelectMany(file => marks.Matches(file.Source).Select(match => $"{file.Relative} {Squeezed(match.Value)}"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string Squeezed(string matched) => Spaces().Replace(matched, string.Empty);

    private static IEnumerable<SourceFile> Scanned(string directory)
        => Directory
            .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .Select(file => new SourceFile("/" + Path.GetRelativePath(directory, file).Replace('\\', '/'), File.ReadAllText(file)))
            .Where(file => file.Relative.Contains(FeatureFolder, StringComparison.Ordinal));

    private static bool IsBuildOutput(string path)
    {
        string[] segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Contains("obj", StringComparer.Ordinal) || segments.Contains("bin", StringComparer.Ordinal);
    }

    [GeneratedRegex(
        @"\bDirectory\s*\.\s*(EnumerateFiles|GetFiles|EnumerateDirectories|GetDirectories|EnumerateFileSystemEntries|GetFileSystemEntries)\b"
        + @"|\.\s*(EnumerateFiles|GetFiles|EnumerateDirectories|GetDirectories|EnumerateFileSystemInfos|GetFileSystemInfos)\s*\("
        + @"|\bnew\s+DirectoryInfo\b|\bEnumerationOptions\b|\bMatcher\b|\bFileSystemWatcher\b")]
    private static partial Regex Walks();

    [GeneratedRegex(
        @"\bFile\s*\.\s*Delete\b|\bDirectory\s*\.\s*Delete\b|\.\s*Delete\s*\(|\bunlink\b|\bExecuteDelete\w*\s*\(")]
    private static partial Regex Deletes();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    private readonly record struct SourceFile(string Relative, string Source);
}
