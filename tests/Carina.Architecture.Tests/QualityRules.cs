using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public static partial class QualityRules
{
    public const string FeatureFolder = "/Quality/";

    public static readonly IReadOnlyList<string> FeatureNamespaces =
    [
        "Carina.Api.Controllers.Quality",
        "Carina.Api.Responder.Quality",
    ];

    public static readonly IReadOnlyList<string> WhereTheWholeApplicationIsWired =
        ["/Program.cs", "ServiceCollectionExtensions.cs"];

    public static readonly IReadOnlyList<string> WhereTheQualityTablesAreLaidOut =
    [
        "/Carina.Infrastructure/Persistence/Configurations/QualityIncidentConfiguration.cs",
        "/Carina.Infrastructure/Persistence/Configurations/QualitySessionMeasurementConfiguration.cs",
        "/Carina.Infrastructure/Persistence/Configurations/QualitySignalRollupConfiguration.cs",
        "/Carina.Infrastructure/Persistence/Configurations/QualitySignalSampleConfiguration.cs",
        "/Carina.Infrastructure/Persistence/Configurations/QualityThresholdConfiguration.cs",
    ];

    public static readonly IReadOnlyList<string> WritersOfWhatIsNotQualitys =
    [
        "IRecordingRepository",
        "RecordingRepository",
        "IRecordingFileEraser",
        "DriverRecordingFileEraser",
        "RecordingRound",
        "IProgrammeRepository",
        "ProgrammeRepository",
        "ProgrammeWriter",
        "IStreamVisitRepository",
        "StreamVisitRepository",
        "IReservationRepository",
        "ReservationRepository",
        "IReservationOutcomeRepository",
        "IRuleRepository",
        "RuleRepository",
        "IBroadcastServiceRepository",
        "BroadcastServiceRepository",
        "ISatelliteTransportStreamRepository",
        "IServiceReachSettingsRepository",
        "ScanApplier",
        "TunerLedgerService",
        "CarinaDbContext",
    ];

    public static readonly IReadOnlyList<string> VerbsThatWriteWhatIsNotQualitys =
    [
        "AbsorbAsync",
        "WithdrawAsync",
        "EraseRecordingAsync",
        "ReplaceTunerLedgerAsync",
        "ToggleTunerAsync",
        "IllustrateAsync",
        "ForgetEverythingAsync",
    ];

    public static readonly IReadOnlyList<string> TheOneLedgerQualityMayWriteTo =
    [
        "ICandidateChannelRepository",
        "CandidateChannelRepository",
        "CandidateChannel",
    ];

    public static readonly IReadOnlyList<string> AnomaliesOtherDomainsDefine =
    [
        "TuneFailureKind",
        "ServiceReach",
        "RotationState",
        "RecordingOutcome",
        "RecordingQuality",
        "QualityLevel",
        "QualityShares",
        "CompletionEvaluator",
        "RecordingVerdict",
        "VisitOutcome",
        "VisitTally",
        "ReservationOutcomeKind",
        "ReservationOutcomeJudgement",
        "ReservationHealth",
    ];

    private static readonly Regex NamesAWriter = Words(WritersOfWhatIsNotQualitys);

    private static readonly Regex NamesAnAnomalyItDoesNotOwn = Words(AnomaliesOtherDomainsDefine);

    private static readonly Regex CallsAVerbThatWrites = new(
        @"\.\s*(" + string.Join('|', VerbsThatWriteWhatIsNotQualitys) + @")\s*\(",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    public static IReadOnlyList<string> FilesInTheFeature(string directory)
        => Feature(directory).Select(file => file.Relative).Order(StringComparer.Ordinal).ToArray();

    public static IReadOnlyList<string> FilesLayingOutTheQualityTables(string directory)
        => Scanned(directory)
            .Where(file => WhereTheQualityTablesAreLaidOut.Contains(file.Relative, StringComparer.Ordinal))
            .Select(file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> WhatDeclaresAForeignKey(string directory)
        => Scanned(directory)
            .Where(file => WhereTheQualityTablesAreLaidOut.Contains(file.Relative, StringComparer.Ordinal))
            .SelectMany(file => WhatDeclaresAForeignKeyIn(file.Source).Select(found => $"{file.Relative} {found}"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> WhatDeclaresAForeignKeyIn(string source) => Found(source, DeclaresAForeignKey());

    public static IReadOnlyList<string> WhatWritesAnotherDomainsLedger(string directory)
        => Feature(directory)
            .SelectMany(file => WhatWritesAnotherDomainsLedgerIn(file.Source).Select(found => $"{file.Relative} {found}"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> WhatWritesAnotherDomainsLedgerIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return
        [
            .. NamesAWriter.Matches(source)
                .Concat(CallsAVerbThatWrites.Matches(source))
                .Concat(ReachesTheStore().Matches(source))
                .Concat(WritesSql().Matches(source))
                .Select(match => Squeezed(match.Value))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

    public static IReadOnlyList<string> WhatOffersAWayToDeleteSomething(string directory)
        => Feature(directory)
            .SelectMany(file => WhatOffersAWayToDeleteSomethingIn(file.Source).Select(found => $"{file.Relative} {found}"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> WhatOffersAWayToDeleteSomethingIn(string source) => Found(source, OffersADeletion());

    public static IReadOnlyList<string> WhatDecidesAnAnomalyItDoesNotOwn(string directory)
        => Feature(directory)
            .SelectMany(file => WhatDecidesAnAnomalyItDoesNotOwnIn(file.Source).Select(found => $"{file.Relative} {found}"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> WhatDecidesAnAnomalyItDoesNotOwnIn(string source)
        => Found(source, NamesAnAnomalyItDoesNotOwn);

    public static bool BelongsToTheFeature(string relative, string source)
    {
        ArgumentNullException.ThrowIfNull(relative);
        ArgumentNullException.ThrowIfNull(source);

        return relative.Contains(FeatureFolder, StringComparison.Ordinal)
               || (FeatureNamespaces.Any(space => source.Contains(space, StringComparison.Ordinal))
                   && !WiresTheWholeApplication(relative));
    }

    private static Regex Words(IReadOnlyList<string> names)
        => new(
            string.Join('|', names.Select(name => @"\b" + name + @"\b")),
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

    private static IReadOnlyList<string> Found(string source, Regex marks)
    {
        ArgumentNullException.ThrowIfNull(source);

        return
        [
            .. marks.Matches(source)
                .Select(match => Squeezed(match.Value))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

    private static bool WiresTheWholeApplication(string relative)
        => WhereTheWholeApplicationIsWired.Any(root => relative.EndsWith(root, StringComparison.Ordinal));

    private static IEnumerable<SourceFile> Feature(string directory)
        => Scanned(directory).Where(file => BelongsToTheFeature(file.Relative, file.Source));

    private static string Squeezed(string matched) => Spaces().Replace(matched, string.Empty);

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
        @"\bHasForeignKey\b|\bHasPrincipalKey\b|\bHasOne\b|\bHasMany\b|\bWithOne\b|\bWithMany\b"
        + @"|(?i:\bFOREIGN\s+KEY\b|\bREFERENCES\s+\w+)")]
    private static partial Regex DeclaresAForeignKey();

    [GeneratedRegex(
        @"\bDbContext\b|\bDbSet\s*<|\bSaveChanges\w*\s*\(|\bExecuteDelete\w*|\bExecuteUpdate\w*"
        + @"|\bExecuteSql\w*|\bFromSql\w*|\bEntityEntry\b|\.\s*Entry\s*\(")]
    private static partial Regex ReachesTheStore();

    [GeneratedRegex(@"(?i:\bINSERT\s+INTO\b|\bUPDATE\s+\w+\s+SET\b|\bDELETE\s+FROM\b|\bTRUNCATE\s+TABLE\b|\bALTER\s+TABLE\b)")]
    private static partial Regex WritesSql();

    [GeneratedRegex(@"\bHttpDelete\b|\bMapDelete\b|\bEndpointEffect\s*\.\s*Destructive\b")]
    private static partial Regex OffersADeletion();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    private readonly record struct SourceFile(string Relative, string Source);
}
