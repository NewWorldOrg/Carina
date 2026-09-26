using System.Globalization;
using System.Text;

namespace Carina.Domain.Migration;

public static class MigrationTextLoss
{
    private static readonly IReadOnlyList<(int First, int Last)> EnclosedBlocks =
    [
        (0x1F100, 0x1F2FF),
        (0x3220, 0x32FF),
    ];

    public static IReadOnlyList<string> Substitutions { get; } = Written();

    public static bool PastRestoring(string said)
    {
        ArgumentNullException.ThrowIfNull(said);

        foreach (string substitution in Substitutions)
        {
            if (said.Contains(substitution, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static int RowsPastRestoring(IEnumerable<string> carried)
    {
        ArgumentNullException.ThrowIfNull(carried);

        return carried.Count(PastRestoring);
    }

    private static IReadOnlyList<string> Written()
    {
        List<string> found = [];

        foreach ((int first, int last) in EnclosedBlocks)
        {
            for (int codePoint = first; codePoint <= last; codePoint++)
            {
                if (Spelt(codePoint) is { } spelt)
                {
                    found.Add($"[{spelt}]");
                }
            }
        }

        return [.. found.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    private static string? Spelt(int codePoint)
    {
        string enclosed = char.ConvertFromUtf32(codePoint);

        if (CharUnicodeInfo.GetUnicodeCategory(enclosed, 0) is UnicodeCategory.OtherNotAssigned)
        {
            return null;
        }

        string plain = enclosed.Normalize(NormalizationForm.FormKC);

        if (string.Equals(plain, enclosed, StringComparison.Ordinal) || plain.Length is 0 or > 4)
        {
            return null;
        }

        foreach (char letter in plain)
        {
            if (!char.IsLetterOrDigit(letter))
            {
                return null;
            }
        }

        return plain;
    }
}
