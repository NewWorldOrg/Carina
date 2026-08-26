using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public static partial class ThumbnailRules
{
    public const string FeatureFolder = "/Thumbnails/";

    public static readonly IReadOnlyList<string> AllowedToNameTheMachinery =
    [
        "/Carina.Infrastructure/Configuration/ThumbnailOptions.cs",
        "/Carina.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs",
    ];

    public static IReadOnlyList<string> FilesInTheFeature(string directory)
        => Scanned(directory)
            .Where(BelongsToTheFeature)
            .Select(file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> ThumbnailFilesThatSayHowARecordingEnded(string directory)
        => Scanned(directory)
            .Where(BelongsToTheFeature)
            .SelectMany(file => WritesTheResult()
                .Matches(file.Source)
                .Select(match => $"{file.Relative} {Squeezed(match.Value)}"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> FilesOutsideTheFeatureThatReachIntoIt(string directory)
        => Scanned(directory)
            .Where(file => !BelongsToTheFeature(file))
            .Where(file => !AllowedToNameTheMachinery.Contains(file.Relative, StringComparer.Ordinal))
            .Where(file => NamesTheMachinery().IsMatch(file.Source))
            .Select(file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string Squeezed(string matched) => Spaces().Replace(matched, string.Empty);

    private static bool BelongsToTheFeature(SourceFile file)
        => file.Relative.Contains(FeatureFolder, StringComparison.Ordinal);

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

    [GeneratedRegex(@"\.\s*(Settle|Note|Interrupt|Resume|Abort|Measure|Extend|Wrote|Acquire)\s*\(")]
    private static partial Regex WritesTheResult();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(
        @"\bIThumbnailRenderer\b"
        + @"|\bIThumbnailWorklist\b"
        + @"|\bThumbnailJob\b"
        + @"|\bThumbnailPass\b"
        + @"|\bThumbnailPlan\b"
        + @"|\bThumbnailSubject\b"
        + @"|\bThumbnailSettings\b"
        + @"|\bFfmpeg\w*")]
    private static partial Regex NamesTheMachinery();

    private readonly record struct SourceFile(string Relative, string Source);
}
