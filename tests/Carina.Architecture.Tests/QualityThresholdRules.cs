using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public static partial class QualityThresholdRules
{
    public const string WhereTheNumbersLive = "Carina.Domain/Recordings/RecordingQuality.cs";

    public static IReadOnlyList<string> QualityNumbersInsideTheLibraryFeature(string directory)
        => LibraryFeature.Marked(directory, NumbersIn);

    public static IReadOnlyList<string> NumbersIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return
        [
            .. Marks()
                .Matches(source)
                .Select(match => match.Value)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

    [GeneratedRegex(
        @"(?<![\w.])\d[\d_]*\.\d[\d_]*([eE][+-]?\d+)?[dDfFmM]?(?![\w.])"
        + @"|(?<![\w.])\d[\d_]*([eE][+-]?\d+|[dDfFmM])(?![\w])"
        + @"|\bQualityShares\b"
        + @"|\bCcDroppedPackets\b|\bCcTotalPackets\b"
        + @"|\bcc_dropped_packets\b|\bcc_total_packets\b")]
    private static partial Regex Marks();
}
