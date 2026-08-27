using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public static partial class RecordingRules
{
    private const string FeatureFolder = "/Recordings/";

    private const string FeatureNamespace = "Carina.Domain.Recordings";

    public static IReadOnlyList<string> EitReadersInsideTheRecordingFeature(string directory)
        => Scanned(directory)
            .Where(BelongsToTheRecordingFeature)
            .Where(file => ReadsSections().IsMatch(file.Source))
            .Select(file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> GuideWritersInsideTheRecordingFeature(string directory)
        => Scanned(directory)
            .Where(BelongsToTheRecordingFeature)
            .Where(file => WritesTheGuide().IsMatch(file.Source))
            .Select(file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool BelongsToTheRecordingFeature(SourceFile file)
        => file.Relative.Contains(FeatureFolder, StringComparison.Ordinal)
           || file.Source.Contains(FeatureNamespace, StringComparison.Ordinal);

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

    [GeneratedRegex(
        @"\bEventInformationTable\b"
        + @"|\bSectionReader\b"
        + @"|\bSectionAssembler\b"
        + @"|\bCarina\.Broadcast\.Sections\b"
        + @"|\bTableRead<")]
    private static partial Regex ReadsSections();

    [GeneratedRegex(
        @"\bIProgrammeRepository\b"
        + @"|\bProgrammeWriter\b"
        + @"|\b(Db)?Set<\s*Programme\s*>"
        + @"|(?i:INSERT\s+INTO\s+programme\b)"
        + @"|(?i:UPDATE\s+programme\b)")]
    private static partial Regex WritesTheGuide();

    private readonly record struct SourceFile(string Relative, string Source);
}
