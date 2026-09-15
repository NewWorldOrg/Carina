using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

/// <summary>
/// Reads the encode feature for a write to the recording it was made from. Encoding is downstream
/// of recording: a job that fails, is called off or dies with its process leaves how the recording
/// ended, why, and what its file weighed exactly as the recording left them. So nothing in the
/// feature tells a recording how it ended, adds a reason to it, moves it along, or hands it back to
/// the recording port to be written. The feature is its folders plus any file named for it,
/// wherever it sits. Like the other rules here it reads source text: it sees the ordinary spellings
/// — a call on something named for a recording, an outcome or a reason spelled at the call, the
/// write verbs on whatever name the port is held under, raw SQL — and no others.
/// </summary>
public static partial class EncodeRecordingFenceRules
{
    public const string FeatureFolder = EncodeDispatchRules.FeatureFolder;

    public const string SurfaceFolder = "/Encoding/";

    public const string FeaturePrefix = EncodeDispatchRules.FeaturePrefix;

    public static IReadOnlyList<string> WhatWritesTheRecordingItWasMadeFrom(string directory)
        => Scanned(directory)
            .SelectMany(file => Writes(file.Source).Select(mark => $"{file.Relative} {mark}"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> HoldersOfTheRecordingPort(string directory)
        => Scanned(directory)
            .Where(file => HoldsThePort().IsMatch(file.Source))
            .Select(file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> FilesInTheFeature(string directory)
        => Scanned(directory).Select(file => file.Relative).Order(StringComparer.Ordinal).ToArray();

    public static IReadOnlyList<string> Writes(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        IEnumerable<string> throughThePort = HoldsThePort()
            .Matches(source)
            .Select(held => held.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .SelectMany(name => Regex
                .Matches(
                    source,
                    @"\b" + Regex.Escape(name) + @"\s*\.\s*(?:SaveAsync|AddAsync)\s*\(",
                    RegexOptions.None,
                    TimeSpan.FromSeconds(5))
                .Select(match => match.Value));

        return
        [
            .. MovesARecording()
                .Matches(source)
                .Select(match => match.Value)
                .Concat(throughThePort)
                .Select(Squeezed)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

    private static string Squeezed(string matched) => Spaces().Replace(matched, string.Empty);

    private static IEnumerable<SourceFile> Scanned(string directory)
        => Directory
            .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .Select(file => new SourceFile("/" + Path.GetRelativePath(directory, file).Replace('\\', '/'), File.ReadAllText(file)))
            .Where(file => IsInTheFeature(file.Relative));

    private static bool IsInTheFeature(string relative)
        => relative.Contains(FeatureFolder, StringComparison.Ordinal)
            || relative.Contains(SurfaceFolder, StringComparison.Ordinal)
            || relative[relative.LastIndexOf('/')..].StartsWith(FeaturePrefix, StringComparison.Ordinal);

    private static bool IsBuildOutput(string path)
    {
        string[] segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Contains("obj", StringComparer.Ordinal) || segments.Contains("bin", StringComparer.Ordinal);
    }

    [GeneratedRegex(@"\bIRecordingRepository\s+(?<name>\w+)")]
    private static partial Regex HoldsThePort();

    [GeneratedRegex(
        @"\.\s*Settle\s*\(\s*RecordingOutcome\s*\."
        + @"|\.\s*Note\s*\(\s*new\s+OutcomeDetail\b"
        + @"|\b\w*[Rr]ecord\w*\s*\.\s*(?:Settle|Note|Abort|Extend|Wrote|Measure|Interrupt|Resume|Acquire|Illustrate|Erased)\s*\("
        + @"|\bSet\s*<\s*Recording\s*>\s*\(\s*\)[\s\S]{0,200}?\bExecuteUpdate\w*\s*\("
        + @"|(?i:\bUPDATE\s+recording\b)")]
    private static partial Regex MovesARecording();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    private readonly record struct SourceFile(string Relative, string Source);
}
