using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public static partial class LibraryQualityRules
{
    public const string WhereTheVerdictIsAskedFor = "/Carina.Domain/Recordings/RecordingQuality.cs";

    public const string WhereTheRecordingTableIsLaidOut =
        "/Carina.Infrastructure/Persistence/Configurations/RecordingConfiguration.cs";

    public const string TheEnumTheLibraryReads = "QualityLevel";

    public static readonly IReadOnlyList<string> WhatBuildsOrFiltersARow =
    [
        "/Carina.Api/Controllers/Recordings/GetRecordingAction.cs",
        "/Carina.Api/Controllers/Recordings/ListRecordingsAction.cs",
        "/Carina.Api/Responder/Recordings/RecordingDetailResponder.cs",
        "/Carina.Api/Responder/Recordings/RecordingResponder.cs",
        "/Carina.Domain/Recordings/RecordingQuery.cs",
        "/Carina.Infrastructure/Persistence/Repositories/RecordingDirectory.cs",
    ];

    public static readonly IReadOnlyList<string> TheFourLevelsTheLibraryReads =
        ["Good", "Unmeasured", "Warning", "MayNotBeWatchable"];

    public static IReadOnlyList<string> WhatDecidesAStandingWhereARowIsBuilt(string directory)
        => Marked(directory, WhatDecidesAStandingIn);

    public static IReadOnlyList<string> WhatDecidesAStandingIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return Found(NamesAStandingMaker().Matches(source));
    }

    public static IReadOnlyList<string> SharesWhereARowIsBuilt(string directory)
        => Marked(directory, SharesIn);

    public static IReadOnlyList<string> SharesIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return Found(ASharePutInTheSource().Matches(source));
    }

    public static IReadOnlyList<string> TheLevelsTheLibraryReads(string directory)
        => [.. NamesAMember()
            .Matches(BodyOf(WhatTheOneAskingPlaceReads(directory), @"enum\s+" + TheEnumTheLibraryReads))
            .Select(match => match.Groups[1].Value)];

    public static IReadOnlyList<string> WhatStoresAStandingOnTheRecordingTable(string directory)
        => Found(StoresAStanding().Matches(Read(directory, WhereTheRecordingTableIsLaidOut)));

    public static IReadOnlyList<string> ComputedColumnsOnTheRecordingTable(string directory)
        => Found(DeclaresAComputedColumn().Matches(Read(directory, WhereTheRecordingTableIsLaidOut)));

    public static string WhatTheOneAskingPlaceReads(string directory) => Read(directory, WhereTheVerdictIsAskedFor);

    public static IReadOnlyList<string> FilesMissingFromTheRowPath(string directory)
        => [.. WhatBuildsOrFiltersARow
            .Append(WhereTheVerdictIsAskedFor)
            .Append(WhereTheRecordingTableIsLaidOut)
            .Where(relative => !File.Exists(Full(directory, relative)))
            .Order(StringComparer.Ordinal)];

    private static IReadOnlyList<string> Marked(string directory, Func<string, IReadOnlyList<string>> marks)
        => [.. WhatBuildsOrFiltersARow
            .SelectMany(relative => marks(Read(directory, relative)).Select(found => $"{relative} {found}"))
            .Order(StringComparer.Ordinal)];

    private static IReadOnlyList<string> Found(MatchCollection matches)
        =>
        [
            .. matches
                .Select(match => match.Value.Trim())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

    private static string BodyOf(string source, string declaration)
    {
        Match found = Regex.Match(source, @"\b" + declaration + @"\b[^{]*\{", RegexOptions.None, TimeSpan.FromSeconds(5));

        if (!found.Success)
        {
            throw new InvalidOperationException($"No declaration matching {declaration} was found to read.");
        }

        int depth = 1;

        for (int at = found.Index + found.Length; at < source.Length; at++)
        {
            depth += source[at] switch
            {
                '{' => 1,
                '}' => -1,
                _ => 0,
            };

            if (depth is 0)
            {
                return source[(found.Index + found.Length)..at];
            }
        }

        throw new InvalidOperationException($"The declaration matching {declaration} is never closed.");
    }

    private static string Full(string directory, string relative)
        => Path.Combine(directory, relative.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

    private static string Read(string directory, string relative) => File.ReadAllText(Full(directory, relative));

    [GeneratedRegex(
        @"\bQualityMetrics?\b"
        + @"|\bQualityStandings?\b"
        + @"|\bQualityThresholdKey\b"
        + @"|\bQualityThresholdShapes?\b"
        + @"|\bQualityThresholdStanding\b"
        + @"|\bThresholdBand\b"
        + @"|\bThresholdEvaluator\b"
        + @"|\bThresholdVerdict\b")]
    private static partial Regex NamesAStandingMaker();

    [GeneratedRegex(
        @"(?<![\w.])\d[\d_]*\.\d[\d_]*([eE][+-]?\d+)?[dDfFmM]?(?![\w.])"
        + @"|(?<![\w.])\d[\d_]*([eE][+-]?\d+|[dDfFmM])(?![\w])")]
    private static partial Regex ASharePutInTheSource();

    [GeneratedRegex(@"^\s*(\w+)\s*=", RegexOptions.Multiline)]
    private static partial Regex NamesAMember();

    [GeneratedRegex(
        @"HasColumnName\s*\(\s*""[^""]*(?:quality|level)[^""]*""\s*\)"
        + @"|HasComputedColumnSql\s*\([^;]{0,400}?(?:quality|level)",
        RegexOptions.IgnoreCase)]
    private static partial Regex StoresAStanding();

    [GeneratedRegex(@"\bHasComputedColumnSql\b")]
    private static partial Regex DeclaresAComputedColumn();
}
