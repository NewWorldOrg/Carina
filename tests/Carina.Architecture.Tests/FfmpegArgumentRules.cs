using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public static class FfmpegArgumentRules
{
    public const string BuilderSuffix = "Invocation.cs";

    private static readonly Regex Interpolated = new(
        @"\$""(?:[^""\\\n]|\\.)*""",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex Hole = new(@"\{(?<filled>[^{}]*)\}", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static readonly Regex Added = new(@"""\s*\+|\+\s*[@$]?""", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static readonly Regex Assembled = new(
        @"\bstring\s*\.\s*(Join|Concat|Format)\s*\(|\bnew\s+StringBuilder\b",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex Canvas = new(@"-canvas_size", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static readonly Regex Font = new(@"(?<![\w-])-font\b", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static readonly Regex ReadAgain = new(
        @"\bUseShellExecute\s*=\s*true\b|/bin/sh\b|/bin/bash\b|\bcmd\.exe\b|\bArguments\s*=(?!=)",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex Spaces = new(@"\s+", RegexOptions.None, TimeSpan.FromSeconds(5));

    public static IReadOnlyList<string> BuildersOfACommandLine(string directory)
        => Scanned(directory)
            .Where(IsABuilder)
            .Select(file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> WhatFillsACommandLine(string directory)
        => Scanned(directory)
            .Where(IsABuilder)
            .SelectMany(file => WhatFillsACommandLineIn(file.Source).Select(filler => $"{file.Relative} {filler}"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> WhatFillsACommandLineIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return
        [
            .. Interpolated
                .Matches(source)
                .SelectMany(match => Hole.Matches(WithoutEscapedBraces(match.Value)))
                .Select(hole => "{" + Squeezed(hole.Groups["filled"].Value) + "}")
                .Concat(Added.Matches(source).Select(_ => "+"))
                .Concat(Assembled.Matches(source).Select(match => Squeezed(match.Value)))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

    public static IReadOnlyList<string> WhatSetsASubtitleCanvas(string directory)
        => Reported(directory, Canvas);

    public static IReadOnlyList<string> WhatNamesAFont(string directory)
        => Reported(directory, Font);

    public static IReadOnlyList<string> WhatCouldMakeACommandBeReadAgainAsText(string directory)
        => Reported(directory, ReadAgain);

    private static IReadOnlyList<string> Reported(string directory, Regex looking)
        => Scanned(directory)
            .SelectMany(file => looking
                .Matches(file.Source)
                .Select(match => $"{file.Relative} {Squeezed(match.Value)}"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool IsABuilder(SourceFile file)
        => file.Relative.EndsWith(BuilderSuffix, StringComparison.Ordinal);

    private static string WithoutEscapedBraces(string literal)
        => literal.Replace("{{", string.Empty, StringComparison.Ordinal)
            .Replace("}}", string.Empty, StringComparison.Ordinal);

    private static string Squeezed(string matched) => Spaces.Replace(matched, string.Empty);

    private static IEnumerable<SourceFile> Scanned(string directory)
        => Directory
            .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .Select(file => new SourceFile(
                "/" + Path.GetRelativePath(directory, file).Replace('\\', '/'),
                File.ReadAllText(file)));

    private static bool IsBuildOutput(string path)
    {
        string[] segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Contains("obj", StringComparer.Ordinal) || segments.Contains("bin", StringComparer.Ordinal);
    }

    private readonly record struct SourceFile(string Relative, string Source);
}
