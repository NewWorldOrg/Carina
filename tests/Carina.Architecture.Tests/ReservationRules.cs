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

    public const string ProjectionTrigger = "recording_projects_its_outcome";

    public const string GuardDefinition =
        "/Carina.Infrastructure/Persistence/Configurations/RecordingGuards.cs";

    private const string Migrations = "/Carina.Db/Migrations/";

    private static readonly IReadOnlyList<string> ReservationFolders = ["/Reservations/", "/Rules/"];

    private static readonly IReadOnlyList<string> ReservationNamespaces =
    [
        "Carina.Domain.Reservations",
        "Carina.Domain.Rules",
    ];

    public static IReadOnlyList<string> WritersOfWhatRecordingOwns(string directory)
        => Scanned(directory)
            .Where(WritesARecordingOwnedColumn)
            .Where(file => !AllowedToWriteThem.Any(allowed => file.Relative.Contains(allowed, StringComparison.Ordinal)))
            .Where(file => !InstallsTheProjection(file))
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

    private static bool InstallsTheProjection(SourceFile file)
        => file.Source.Contains(ProjectionTrigger, StringComparison.Ordinal)
           && (file.Relative.StartsWith(Migrations, StringComparison.Ordinal)
               || string.Equals(file.Relative, GuardDefinition, StringComparison.Ordinal));

    private static bool BelongsToTheReservationFeature(SourceFile file)
        => ReservationFolders.Any(folder => file.Relative.Contains(folder, StringComparison.Ordinal))
           || ReservationNamespaces.Any(space => file.Source.Contains(space, StringComparison.Ordinal));

    /// <summary>
    /// The SQL forms name the table's own columns, so they are read everywhere. The typed form
    /// names a property, and more than one table has a <c>StartedAt</c>: a typed write of it counts
    /// only where the reservation is named, because a typed write over the reservation table
    /// cannot be spelt without naming it. The outcome has no namesake and counts everywhere.
    /// </summary>
    private static bool WritesARecordingOwnedColumn(SourceFile file)
        => UpdatesTheClaim().IsMatch(file.Source)
           || UpdatesTheOutcome().IsMatch(file.Source)
           || SetsTheOutcome().IsMatch(file.Source)
           || (SetsTheClaim().IsMatch(file.Source) && NamesTheReservation(file));

    private static bool NamesTheReservation(SourceFile file)
        => BelongsToTheReservationFeature(file) || NamesTheReservationType().IsMatch(file.Source);

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

    [GeneratedRegex(@"SetProperty[\s\S]{0,120}?\.RecordingOutcome\b")]
    private static partial Regex SetsTheOutcome();

    [GeneratedRegex(@"SetProperty[\s\S]{0,120}?\.StartedAt\b")]
    private static partial Regex SetsTheClaim();

    [GeneratedRegex(@"\bReservation\b")]
    private static partial Regex NamesTheReservationType();

    [GeneratedRegex(
        @"\bbool\s+(Matches|IsMatch|Satisfies|Accepts)\b"
        + @"|\bFunc<\s*(Programme|ProgrammeMatch)\b"
        + @"|\bIQueryable<\s*(Programme|ProgrammeMatch)\s*>")]
    private static partial Regex Matcher();

    private readonly record struct SourceFile(string Relative, string Source);
}
