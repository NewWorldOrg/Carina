using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public static partial class LibraryFeature
{
    public const string Folder = "/Library/";

    public const string CriteriaType = "RecordingSearchCriteria";

    public static IReadOnlyList<SourceFile> Files(string directory)
        => [.. Scanned(directory).Where(Belongs)];

    public static IReadOnlyList<string> Marked(string directory, Func<string, IReadOnlyList<string>> marks)
    {
        ArgumentNullException.ThrowIfNull(marks);

        return
        [
            .. Files(directory)
                .SelectMany(file => marks(file.Source).Select(mark => $"{file.Relative} {mark}"))
                .Order(StringComparer.Ordinal),
        ];
    }

    private static bool Belongs(SourceFile file)
        => file.Relative.Contains(Folder, StringComparison.Ordinal)
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

    public readonly record struct SourceFile(string Relative, string Source);
}
