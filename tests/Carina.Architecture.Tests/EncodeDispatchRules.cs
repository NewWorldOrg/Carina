using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

/// <summary>
/// Reads the encode feature for the three ways a job could get away from the ledger: a job moved
/// to running anywhere but by the ledger's conditional update, a file moved or copied into place
/// anywhere but by the placer that writes the ledger first, and a programme started anywhere but
/// by the one run that hands the ledger the programme's identity before reading a line from it.
/// The feature is its folders plus any file named for it, wherever it sits. Like the other rules
/// here it reads source text, so it sees the ordinary spellings and no others.
/// </summary>
public static partial class EncodeDispatchRules
{
    public const string FeatureFolder = "/Encodings/";

    public const string FeaturePrefix = "/Encode";

    public static IReadOnlyList<string> WhatMovesAJobToRunning(string directory)
        => Reported(directory, MovesToRunning());

    public static IReadOnlyList<string> WhatSpellsRunningForTheDatabase(string directory)
        => Reported(directory, SpellsRunning());

    public static IReadOnlyList<string> WhatPutsAFileSomewhere(string directory)
        => Reported(directory, PutsAFile());

    public static IReadOnlyList<string> WhatNamesTheArtefact(string directory)
        => Reported(directory, NamesTheArtefact());

    public static IReadOnlyList<string> WhatStartsAProgramme(string directory)
        => Reported(directory, StartsAProgramme());

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

    [GeneratedRegex(@"(?<![=!<>])=\s*EncodeJobStatus\s*\.\s*Running\b|SetProperty\s*\([^;]*?EncodeJobStatus\s*\.\s*Running\b")]
    private static partial Regex MovesToRunning();

    [GeneratedRegex(@"'Running'")]
    private static partial Regex SpellsRunning();

    [GeneratedRegex(
        @"\bFile\s*\.\s*(Move|Copy|Replace|CreateSymbolicLink)\b|\.\s*(MoveTo|CopyTo|Replace)\s*\(\s*[^)]*[Pp]ath|\brename\s*\(|\blink\s*\(")]
    private static partial Regex PutsAFile();

    [GeneratedRegex(@"\bEncodeFileName\s*\.\s*Artefact\s*\(")]
    private static partial Regex NamesTheArtefact();

    [GeneratedRegex(@"\bAnotherProgramme\s*\.\s*(Start|SayAsync)\s*\(|\bProcess\s*\.\s*Start\s*\(|\bnew\s+Process\s*[({]|\bTranscoderProcess\s*\.\s*(Start|Launch)\s*\(")]
    private static partial Regex StartsAProgramme();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    private readonly record struct SourceFile(string Relative, string Source);
}
