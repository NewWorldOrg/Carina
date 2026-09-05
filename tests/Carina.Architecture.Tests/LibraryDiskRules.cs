using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public static partial class LibraryDiskRules
{
    public static IReadOnlyList<string> ReachesForTheDiskInsideTheLibraryFeature(string directory)
        => LibraryFeature.Marked(directory, ReachesIn);

    public static IReadOnlyList<string> ReachesIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return
        [
            .. Marks()
                .Matches(source)
                .Select(match => Spaces().Replace(match.Value, string.Empty))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

    [GeneratedRegex(
        @"\bFile\s*\.\s*\w+"
        + @"|\bDirectory\s*\.\s*\w+"
        + @"|\bnew\s+(FileInfo|DirectoryInfo|DriveInfo)\b"
        + @"|\bFileSystemInfo\b"
        + @"|\bstat\b")]
    private static partial Regex Marks();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
