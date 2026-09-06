using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public static partial class EncodeLedgerRules
{
    public const string WhereTheLedgerLives = "Carina.Infrastructure/Persistence/Repositories/EncodeJobRepository.cs";

    public static IReadOnlyList<string> TheEncodeLedgerReachedIntoFromTheLibraryFeature(string directory)
        => LibraryFeature.Marked(directory, ReachesIn);

    public static IReadOnlyList<string> ReachesIn(string source)
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
        @"\bI?Encode(?:Job|Profile|Destination|Scratch)\w*\b"
        + @"|\bEncodeFileName\b"
        + @"|\bencode_(?:job|profile|destination|scratch_file)\b")]
    private static partial Regex Marks();
}
