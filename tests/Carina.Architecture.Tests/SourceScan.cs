namespace Carina.Architecture.Tests;

public static class SourceScan
{
    public static IReadOnlyList<string> FilesMentioning(string directory, params string[] identifiers)
        => Directory
            .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .Where(file => Mentions(File.ReadAllText(file), identifiers))
            .Select(file => Path.GetRelativePath(directory, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool Mentions(string source, IEnumerable<string> identifiers)
        => identifiers.Any(identifier => source.Contains(identifier, StringComparison.Ordinal));

    private static bool IsBuildOutput(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Contains("obj", StringComparer.Ordinal)
               || segments.Contains("bin", StringComparer.Ordinal);
    }
}
