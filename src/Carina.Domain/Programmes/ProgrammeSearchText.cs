using System.Text;

namespace Carina.Domain.Programmes;

public static class ProgrammeSearchText
{
    public const string Compatibility = "NFKC";

    public const string BetweenNameAndSummary = " ";

    private const char TheLetterTheStoreLowersAndTheRuntimeLeavesStanding = 'İ';

    private const char WhatTheStoreLowersItTo = 'i';

    public static string Searchable(string name, string summary)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(summary);

        return Folded(name + BetweenNameAndSummary + summary);
    }

    public static string Folded(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Normalised(text)
            .ToLowerInvariant()
            .Replace(
                TheLetterTheStoreLowersAndTheRuntimeLeavesStanding,
                WhatTheStoreLowersItTo);
    }

    public static bool Foldable(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        for (int index = 0; index < text.Length; index++)
        {
            if (!Rune.TryGetRuneAt(text, index, out Rune rune) || Refused(rune))
            {
                return false;
            }

            index += rune.Utf16SequenceLength - 1;
        }

        return true;
    }

    private static string Normalised(string text)
    {
        if (Foldable(text))
        {
            return text.Normalize(NormalizationForm.FormKC);
        }

        var carried = new StringBuilder(text.Length);
        int taken = 0;
        int index = 0;

        while (index < text.Length)
        {
            bool read = Rune.TryGetRuneAt(text, index, out Rune rune);
            int width = read ? rune.Utf16SequenceLength : 1;

            if (read && !Refused(rune))
            {
                index += width;
                continue;
            }

            carried.Append(text[taken..index].Normalize(NormalizationForm.FormKC));
            carried.Append(text.AsSpan(index, width));
            index += width;
            taken = index;
        }

        carried.Append(text[taken..].Normalize(NormalizationForm.FormKC));

        return carried.ToString();
    }

    private static bool Refused(Rune rune)
        => rune.Value is >= 0xFDD0 and <= 0xFDEF || (rune.Value & 0xFFFE) == 0xFFFE;
}
