using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public static partial class ReservationRules
{
    public static readonly IReadOnlyList<string> RecordingOwnedColumns = ["started_at", "recording_outcome"];

    public static readonly IReadOnlyList<string> AllowedToWriteThem =
    [
        "/Recordings/",
        "/Migration/",
        "/Carina.Infrastructure/Persistence/Repositories/ReservationRecordingContract.cs",
    ];

    private static readonly IReadOnlyList<string> ReservationFolders = ["/Reservations/", "/Rules/"];

    private static readonly IReadOnlyList<string> ReservationNamespaces =
    [
        "Carina.Domain.Reservations",
        "Carina.Domain.Rules",
    ];

    public static IReadOnlyList<string> WritersOfWhatRecordingOwns(string directory)
        => Scanned(directory)
            .Where(file => WritesARecordingOwnedColumn(file.Source))
            .Where(file => !AllowedToWriteThem.Any(allowed => file.Relative.Contains(allowed, StringComparison.Ordinal)))
            .Select(file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> ProgrammeMatchersOutsideTheGuide(string directory)
        => Scanned(directory)
            .Where(BelongsToTheReservationFeature)
            .Where(file => Matcher().IsMatch(file.Source))
            .Select(file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool BelongsToTheReservationFeature(SourceFile file)
        => ReservationFolders.Any(folder => file.Relative.Contains(folder, StringComparison.Ordinal))
           || ReservationNamespaces.Any(space => file.Source.Contains(space, StringComparison.Ordinal));

    private static bool WritesARecordingOwnedColumn(string source)
        => UpdatesTheClaim().IsMatch(source)
           || UpdatesTheOutcome().IsMatch(source)
           || SetsWhatRecordingOwns().IsMatch(source);

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

    [GeneratedRegex(@"UPDATE[\s\S]{0,300}?\bSET\b[\s\S]{0,300}?\bstarted_at\b\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex UpdatesTheClaim();

    [GeneratedRegex(@"UPDATE[\s\S]{0,300}?\bSET\b[\s\S]{0,300}?\brecording_outcome\b\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex UpdatesTheOutcome();

    [GeneratedRegex(@"SetProperty[\s\S]{0,120}?\.(StartedAt|RecordingOutcome)\b")]
    private static partial Regex SetsWhatRecordingOwns();

    [GeneratedRegex(
        @"\bbool\s+(Matches|IsMatch|Satisfies|Accepts)\b"
        + @"|\bFunc<\s*(Programme|ProgrammeMatch)\b"
        + @"|\bIQueryable<\s*(Programme|ProgrammeMatch)\s*>")]
    private static partial Regex Matcher();

    private readonly record struct SourceFile(string Relative, string Source);
}
