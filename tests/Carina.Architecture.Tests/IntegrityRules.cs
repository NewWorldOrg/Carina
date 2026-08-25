using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public static partial class IntegrityRules
{
    public const string FeatureFolder = "/Integrity/";

    public static readonly IReadOnlyList<string> AllowedToWriteAFile = [];

    public static IReadOnlyList<string> FilesThatCouldDeleteSomething(string directory)
        => Scanned(directory)
            .Where(file => file.Relative.Contains(FeatureFolder, StringComparison.Ordinal))
            .Where(file => Deletes().IsMatch(file.Source))
            .Select(file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> FilesThatCouldWriteSomethingTheyMayNot(string directory)
        => Scanned(directory)
            .Where(file => file.Relative.Contains(FeatureFolder, StringComparison.Ordinal))
            .Where(file => !AllowedToWriteAFile.Contains(file.Relative, StringComparer.Ordinal))
            .Where(file => Writes().IsMatch(file.Source))
            .Select(file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

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

    [GeneratedRegex(@"\.Delete\s*\(|\bUnlink\s*\(|\bRecycle\s*\(")]
    private static partial Regex Deletes();

    [GeneratedRegex(
        @"\bFile\.(Move|Replace|Copy|Create|CreateText|Open|OpenWrite|AppendText|WriteAll\w+|AppendAll\w+)\s*\("
        + @"|\bDirectory\.(CreateDirectory|Move)\s*\("
        + @"|\bnew\s+(FileStream|StreamWriter)\b"
        + @"|\.SetLength\s*\("
        + @"|\bFileMode\.")]
    private static partial Regex Writes();

    private readonly record struct SourceFile(string Relative, string Source);
}
