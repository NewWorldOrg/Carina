using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public static partial class QualityThresholdRules
{
    public const string FeatureFolder = "/Library/";

    public const string CriteriaType = "RecordingSearchCriteria";

    public const string WhereTheNumbersLive = "Carina.Domain/Recordings/RecordingQuality.cs";

    public static IReadOnlyList<string> QualityNumbersInsideTheLibraryFeature(string directory)
        => Scanned(directory)
            .Where(BelongsToTheLibraryFeature)
            .SelectMany(file => NumbersIn(file.Source).Select(number => $"{file.Relative} {number}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> FilesOfTheLibraryFeature(string directory)
        => Scanned(directory)
            .Where(BelongsToTheLibraryFeature)
            .Select(file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> NumbersIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return
        [
            .. Marks()
                .Matches(source)
                .Select(match => match.Value)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

    private static bool BelongsToTheLibraryFeature(SourceFile file)
        => file.Relative.Contains(FeatureFolder, StringComparison.Ordinal)
            || FeatureNamespace().IsMatch(file.Source)
            || file.Source.Contains(CriteriaType, StringComparison.Ordinal);

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

    [GeneratedRegex(@"namespace\s+[\w.]+\.Library\s*[;{]")]
    private static partial Regex FeatureNamespace();

    [GeneratedRegex(
        @"(?<![\w.])\d[\d_]*\.\d[\d_]*([eE][+-]?\d+)?[dDfFmM]?(?![\w.])"
        + @"|(?<![\w.])\d[\d_]*([eE][+-]?\d+|[dDfFmM])(?![\w])"
        + @"|\bQualityShares\b"
        + @"|\bCcDroppedPackets\b|\bCcTotalPackets\b"
        + @"|\bcc_dropped_packets\b|\bcc_total_packets\b")]
    private static partial Regex Marks();

    private readonly record struct SourceFile(string Relative, string Source);
}
