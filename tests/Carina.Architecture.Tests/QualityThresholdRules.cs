using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public static partial class QualityThresholdRules
{
    public const string WhereTheNumbersLive = "Carina.Domain/Quality/QualityThresholdShape.cs";

    public const string WhereTheMigrationsLive = "Carina.Db/Migrations/";

    public static readonly IReadOnlyList<string> WhatARecordingIsJudgedOn = ["Scrambl", "scrambl", "PacketsLost"];

    private const string AShareWrittenDown =
        @"(?<![\w.])\d[\d_]*\.\d[\d_]*([eE][+-]?\d+)?[dDfFmM]?(?![\w.])"
        + @"|(?<![\w.])\d[\d_]*([eE][+-]?\d+|[dDfFmM])(?![\w])";

    public static IReadOnlyList<string> QualityNumbersInsideTheLibraryFeature(string directory)
        => LibraryFeature.Marked(directory, NumbersIn);

    public static IReadOnlyList<string> NumbersIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return Distinct(Marks().Matches(source));
    }

    public static IReadOnlyList<string> SharesOfWhatARecordingIsJudgedOnOutsideTheirTable(string directory)
        =>
        [
            .. SourceScan.FilesMentioning(directory, [.. WhatARecordingIsJudgedOn])
                .Where(relative => relative != WhereTheNumbersLive
                    && !relative.StartsWith(WhereTheMigrationsLive, StringComparison.Ordinal))
                .SelectMany(relative => SharesOfWhatARecordingIsJudgedOnIn(File.ReadAllText(Path.Combine(directory, relative)))
                    .Select(share => $"/{relative} {share}"))
                .Order(StringComparer.Ordinal),
        ];

    public static IReadOnlyList<string> SharesOfWhatARecordingIsJudgedOnIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return WhatARecordingIsJudgedOn.Any(measure => source.Contains(measure, StringComparison.Ordinal))
            ? Distinct(Shares().Matches(source))
            : [];
    }

    private static IReadOnlyList<string> Distinct(MatchCollection matches)
        =>
        [
            .. matches
                .Select(match => match.Value)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

    [GeneratedRegex(AShareWrittenDown)]
    private static partial Regex Shares();

    [GeneratedRegex(
        AShareWrittenDown
        + @"|\bQualityThresholdShapes\b"
        + @"|\bCcDroppedPackets\b|\bCcTotalPackets\b"
        + @"|\bcc_dropped_packets\b|\bcc_total_packets\b")]
    private static partial Regex Marks();
}
