using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

/// <summary>
/// Reads the encode feature for the thing BR-ED2-007 forbids it outright: shortening what it
/// writes. A chapter is a label and nothing more, so a break found in a recording has to leave the
/// artefact the length it would have been had nobody looked — a mark that is wrong costs a viewer
/// one mark to ignore, where a cut that is wrong costs them the programme, and there is no setting
/// for cutting and no path that could. What that takes is the absence of the options and filters an
/// output is shortened with, which is what this reports: the durations and frame counts that stop a
/// run early, the filters that keep part of a stream, and the muxer that writes a file per stretch.
/// The feature is its folders plus any file named for it, wherever it sits. Like the other rules
/// here it reads source text, so it sees the ordinary spellings and no others.
/// </summary>
public static partial class EncodeCutRules
{
    public const string FeatureFolder = "/Encodings/";

    public const string FeaturePrefix = "/Encode";

    public static IReadOnlyList<string> WhatShortensAnOutput(string directory)
        => Reported(directory, Shortens());

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
            .Where(file => IsInTheFeature(file.Relative));

    private static bool IsInTheFeature(string relative)
        => relative.Contains(FeatureFolder, StringComparison.Ordinal)
            || relative[relative.LastIndexOf('/')..].StartsWith(FeaturePrefix, StringComparison.Ordinal);

    private static bool IsBuildOutput(string path)
    {
        string[] segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Contains("obj", StringComparer.Ordinal) || segments.Contains("bin", StringComparer.Ordinal);
    }

    [GeneratedRegex(
        @"""-t""|""-to""|""-fs""|""-frames(:[av])?""|""-vframes""|""-aframes""|""-segment_times?""|""segment"""
        + @"|\btrim\s*=|\batrim\s*=|\bconcat\b|\bbetween\s*\(\s*t\b")]
    private static partial Regex Shortens();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    private readonly record struct SourceFile(string Relative, string Source);
}
