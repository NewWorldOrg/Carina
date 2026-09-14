using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public static partial class RecordingFenceRules
{
    public const string TheOneRouteThatThrowsARecordingAway = "api/recordings/{id}";

    public const string TheActionOnThatRoute =
        "/Carina.Api/Controllers/Recordings/DeleteRecordingAction.cs";

    public const string TheServiceThatGuardsIt = "/Carina.Api/Services/RecordingService.cs";

    public const string ErasureTheGuardedRouteReaches =
        "/Carina.Infrastructure/Persistence/Repositories/RecordingDirectory.cs";

    public const string ReadOnlyProgrammePort = "IAnnouncedProgrammes";

    public const string FullProgrammePort = "IProgrammeRepository";

    public const string ProgrammePortFile = "/Carina.Domain/Programmes/IProgrammeRepository.cs";

    public const string TheRoundThatReadsTheGuide = "/Carina.Infrastructure/Recordings/RecordingRound.cs";

    public static readonly IReadOnlyList<string> WriteMembersOfTheGuide =
    [
        "AbsorbAsync",
        "AddAsync",
        "ForgetAsync",
        "ForgetEverythingAsync",
    ];

    private const string FeatureFolder = "/Recordings/";

    private const string FeatureNamespace = "Carina.Domain.Recordings";

    private const string ApiFolder = "/Carina.Api/";

    public static IReadOnlyList<string> WritersOfWhatRecordingOwnsThroughThePropertyBag(string directory)
        => Scanned(directory)
            .Where(WritesOneOfThemSideways)
            .Where(file => !ReservationRules.AllowedToWriteThem.Any(
                allowed => file.Relative.Contains(allowed, StringComparison.Ordinal)))
            .Select(file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> BroadcastTableReadersInsideTheRecordingFeature(string directory)
        => Scanned(directory)
            .Where(BelongsToTheRecordingFeature)
            .Where(file => ReadsABroadcastTable().IsMatch(file.Source))
            .Select(file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> WriteMembersOnThePortTheRoundHolds(string directory)
        => WriteMembersIn(BodyOf(Read(directory, ProgrammePortFile), ReadOnlyProgrammePort));

    public static IReadOnlyList<string> WriteMembersOnThePortCollectionHolds(string directory)
        => WriteMembersIn(BodyOf(Read(directory, ProgrammePortFile), FullProgrammePort));

    public static IReadOnlyList<string> DeletionsOfferedByTheRecordingFeature(string directory)
        => Scanned(directory)
            .Where(BelongsToTheRecordingFeature)
            .Where(file => !string.Equals(file.Relative, TheActionOnThatRoute, StringComparison.Ordinal))
            .Where(file => OffersADeletion().IsMatch(file.Source))
            .Select(file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> RoutesThatThrowARecordingAway(string directory)
        => Scanned(directory)
            .Where(file => file.Relative.StartsWith(ApiFolder, StringComparison.Ordinal))
            .Where(file => OffersADeletion().IsMatch(file.Source))
            .SelectMany(file => RouteOf().Matches(file.Source).Select(match => match.Groups[1].Value))
            .Where(route => route.Contains("recording", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> WhatErasesARecordingLedgerRow(string directory)
        => Scanned(directory)
            .Where(file => ErasesALedgerRow().IsMatch(file.Source))
            .Select(file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> WriteMembersIn(string body)
        => WriteMembersOfTheGuide
            .Where(member => body.Contains(member, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string BodyOf(string source, string port)
    {
        Match declaration = Regex.Match(
            source,
            @"\binterface\s+" + Regex.Escape(port) + @"\b[^{]*\{",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        if (!declaration.Success)
        {
            throw new InvalidOperationException($"No declaration of {port} was found to weigh.");
        }

        int depth = 1;

        for (int at = declaration.Index + declaration.Length; at < source.Length; at++)
        {
            depth += source[at] switch
            {
                '{' => 1,
                '}' => -1,
                _ => 0,
            };

            if (depth is 0)
            {
                return source[(declaration.Index + declaration.Length)..at];
            }
        }

        throw new InvalidOperationException($"The declaration of {port} is never closed.");
    }

    private static string Read(string directory, string relative)
        => File.ReadAllText(Path.Combine(directory, relative.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));

    private static bool WritesOneOfThemSideways(SourceFile file)
        => SetsItThroughAnEntry().IsMatch(file.Source)
           || SetsItThroughTheCurrentValues().IsMatch(file.Source)
           || SetsItThroughAnUntypedSetProperty().IsMatch(file.Source)
           || NamesItInAnInsert().IsMatch(file.Source);

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
        @"\.\s*Property\s*\([^()]{0,160}?(?:started_at|recording_outcome|StartedAt|RecordingOutcome)[^()]{0,60}\)"
        + @"\s*\.\s*CurrentValue\s*=\s*(?!=)")]
    private static partial Regex SetsItThroughAnEntry();

    [GeneratedRegex(
        @"CurrentValues\s*\[\s*""?(?:started_at|recording_outcome|StartedAt|RecordingOutcome)""?\s*\]\s*=\s*(?!=)")]
    private static partial Regex SetsItThroughTheCurrentValues();

    [GeneratedRegex(
        @"SetProperty[\s\S]{0,200}?EF\s*\.\s*Property\s*<[^>]*>\s*\([^()]{0,120}?"
        + @"""(?:started_at|recording_outcome|StartedAt|RecordingOutcome)""")]
    private static partial Regex SetsItThroughAnUntypedSetProperty();

    [GeneratedRegex(
        @"(?i:INSERT\s+INTO\s+reservation\b)[\s\S]{0,600}?\b(?:started_at|recording_outcome)\b")]
    private static partial Regex NamesItInAnInsert();

    [GeneratedRegex(
        @"\bCarina\.Broadcast\.Tables\b"
        + @"|\bDescribedEvent\b"
        + @"|\bEventInformationTable\b"
        + @"|\bNetworkInformationTable\b"
        + @"|\bServiceDescriptionTable\b"
        + @"|\bCommonDataTable\b"
        + @"|\bTableDefect\b"
        + @"|\bTableRead\s*<")]
    private static partial Regex ReadsABroadcastTable();

    [GeneratedRegex(@"\bHttpDelete\b|\bMapDelete\b")]
    private static partial Regex OffersADeletion();

    [GeneratedRegex(@"\bRoute\s*\(\s*""([^""]+)""\s*\)")]
    private static partial Regex RouteOf();

    [GeneratedRegex(@"Set<\s*Recording\s*>[\s\S]{0,400}?\bExecuteDelete\w*\s*\(")]
    private static partial Regex ErasesALedgerRow();

    private readonly record struct SourceFile(string Relative, string Source);
}
