namespace Carina.Architecture.Tests;

public static class SourceScan
{
    public static IReadOnlyList<string> FilesMentioning(string directory, params string[] identifiers)
        => Matching(directory, source => Mentions(source, identifiers));

    public static IReadOnlyList<string> FilesMentioningAll(string directory, params string[] identifiers)
        => Matching(directory, source => MentionsAll(source, identifiers));

    public static IReadOnlyList<string> FilesMentioningBoth(
        string directory,
        IEnumerable<string> these,
        IEnumerable<string> those)
        => Matching(directory, source => Mentions(source, these) && Mentions(source, those));

    private static IReadOnlyList<string> Matching(string directory, Func<string, bool> predicate)
        => Directory
            .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .Where(file => predicate(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(directory, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool Mentions(string source, IEnumerable<string> identifiers)
        => identifiers.Any(identifier => source.Contains(identifier, StringComparison.Ordinal));

    private static bool MentionsAll(string source, IEnumerable<string> identifiers)
        => identifiers.All(identifier => source.Contains(identifier, StringComparison.Ordinal));

    private static bool IsBuildOutput(string path)
    {
        string[] segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Contains("obj", StringComparer.Ordinal)
               || segments.Contains("bin", StringComparer.Ordinal);
    }
}
