using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public static class ThumbnailRules
{
    public const string FeatureFolder = "/Thumbnails/";

    public const string NamedForThumbnails = "Thumbnail";

    public static readonly IReadOnlyList<string> WaysToSayHowARecordingEnded =
        ["Settle", "Note", "Interrupt", "Resume", "Abort", "Measure", "Extend", "Wrote", "Acquire"];

    public static readonly IReadOnlyList<string> WaysToReachPastTheAggregate =
        ["GetProperty", "GetField", "GetMethod", "SetValue", "CreateInstance", "ExecuteSql", "FromSql", "Entry", "Property"];

    public static readonly IReadOnlyList<string> WaysToWriteAValuePastTheAggregate =
        ["CurrentValue", "OriginalValue"];

    public static readonly IReadOnlyList<string> Machinery =
    [
        "IThumbnailRenderer",
        "IThumbnailWorklist",
        "ThumbnailWorklist",
        "ThumbnailJob",
        "ThumbnailPass",
        "ThumbnailPlan",
        "ThumbnailIntent",
        "ThumbnailSubject",
        "ThumbnailSettings",
        "ThumbnailRender",
        "ThumbnailRequest",
        "ThumbnailRemake",
        "IThumbnailRemaker",
        "ThumbnailOptions",
        "ThumbnailValidation",
        "FfmpegInvocation",
        "FfmpegThumbnailRenderer",
    ];

    public static readonly IReadOnlyList<string> AllowedToNameTheMachinery =
    [
        "/Carina.Infrastructure/Configuration/ThumbnailOptions.cs",
        "/Carina.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs",
        "/Carina.Api/Services/RecordingService.cs",
        "/Carina.Api/Responder/Recordings/RecordingDetailResponder.cs",
        "/Carina.Infrastructure/Recordings/LocalRecordingFileEraser.cs",
    ];

    private static readonly Regex ReachesForTheResult = new(
        @"\.\s*(" + string.Join('|', WaysToSayHowARecordingEnded.Concat(WaysToReachPastTheAggregate)) + @")\w*\s*\(",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex WritesAValuePastTheAggregate = new(
        @"\.\s*(" + string.Join('|', WaysToWriteAValuePastTheAggregate) + @")\b",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex NamesTheMachinery = new(
        string.Join('|', Machinery.Select(name => @"\b" + name + @"\b")),
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex DeclaresAType = new(
        @"^public\s+(?:static\s+|sealed\s+|abstract\s+|partial\s+|readonly\s+|ref\s+)*"
        + @"(?:class|record\s+struct|record|struct|enum|interface|delegate)\s+"
        + @"(?:[\w<>?\[\],\s]+?\s+)?(?<named>\w+)\s*[<({:]",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    private static readonly Regex Spaces = new(@"\s+", RegexOptions.None, TimeSpan.FromSeconds(5));

    public static IReadOnlyList<string> FilesInTheFeature(string directory)
        => Scanned(directory)
            .Where(file => file.Relative.Contains(FeatureFolder, StringComparison.Ordinal))
            .Select(file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> TypesTheFeatureDeclares(string directory)
        => Scanned(directory)
            .Where(file => file.Relative.Contains(FeatureFolder, StringComparison.Ordinal)
                           || (IsNamedForThumbnails(file)
                               && AllowedToNameTheMachinery.Contains(file.Relative, StringComparer.Ordinal)))
            .SelectMany(file => DeclaresAType.Matches(file.Source).Select(match => match.Groups["named"].Value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> FilesNamedForThumbnails(string directory)
        => Scanned(directory)
            .Where(IsNamedForThumbnails)
            .Select(file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> WhatNamedForThumbnailsReachesForARecordingsResult(string directory)
        => Scanned(directory)
            .Where(IsNamedForThumbnails)
            .SelectMany(file => ReachesForTheResult
                .Matches(file.Source)
                .Concat(WritesAValuePastTheAggregate.Matches(file.Source))
                .Select(match => $"{file.Relative} {Squeezed(match.Value)}"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> FilesOutsideTheFeatureThatReachIntoIt(string directory)
        => Scanned(directory)
            .Where(file => !file.Relative.Contains(FeatureFolder, StringComparison.Ordinal))
            .Where(file => !AllowedToNameTheMachinery.Contains(file.Relative, StringComparer.Ordinal))
            .Where(file => NamesTheMachinery.IsMatch(file.Source))
            .Select(file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool IsNamedForThumbnails(SourceFile file)
        => file.Relative.Contains(NamedForThumbnails, StringComparison.Ordinal);

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
