using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public static partial class RecordingRetryRules
{
    public const string TheDecision = "/Carina.Domain/Recordings/StartRetry.cs";

    public const string TheLedgerSide = "/Carina.Infrastructure/Recordings/RecordingRetries.cs";

    private const string Migrations = "/Carina.Db/Migrations/";

    public static IReadOnlyList<string> FilesOfTheRetry(string directory)
        => Scanned(directory)
            .Where(IsPartOfTheRetry)
            .Select(file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> RetryFilesThatReachARecordingOfTheirOwn(string directory)
        => Scanned(directory)
            .Where(IsPartOfTheRetry)
            .Where(file => ReachesARecording().IsMatch(file.Source))
            .Select(file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool IsPartOfTheRetry(SourceFile file)
        => NamedForTheRetry().IsMatch(file.Relative[(file.Relative.LastIndexOf('/') + 1)..]);

    private static IEnumerable<SourceFile> Scanned(string directory)
        => Directory
            .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .Select(file => new SourceFile(
                "/" + Path.GetRelativePath(directory, file).Replace('\\', '/'),
                File.ReadAllText(file)))
            .Where(file => !file.Relative.Contains(Migrations, StringComparison.Ordinal));

    private static bool IsBuildOutput(string path)
    {
        string[] segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Contains("obj", StringComparer.Ordinal) || segments.Contains("bin", StringComparer.Ordinal);
    }

    [GeneratedRegex(@"Retr(?:y|ies)")]
    private static partial Regex NamedForTheRetry();

    [GeneratedRegex(
        @"\.\s*Settle\s*\("
        + @"|\bRecording\s*\.\s*(?:Begin|Rehydrate)\s*\("
        + @"|\bIRecording(?:Repository|Directory|FileEraser)\b"
        + @"|\bIReservationRecordingContract\b"
        + @"|\bStartSessionAsync\b")]
    private static partial Regex ReachesARecording();

    private readonly record struct SourceFile(string Relative, string Source);
}
