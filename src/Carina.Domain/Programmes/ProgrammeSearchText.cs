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

    private static string Normalised(string text)
    {
        try
        {
            return text.Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            return AroundWhatTheRuntimeWillNotNormalise(text);
        }
    }

    private static string AroundWhatTheRuntimeWillNotNormalise(string text)
    {
        var carried = new StringBuilder(text.Length);
        int taken = 0;
        int index = 0;

        while (index < text.Length)
        {
            bool read = Rune.TryGetRuneAt(text, index, out Rune rune);
            int width = read ? rune.Utf16SequenceLength : 1;
            string one = text.Substring(index, width);

            if (read && Normalises(one))
            {
                index += width;
                continue;
            }

            carried.Append(Normalised(text[taken..index]));
            carried.Append(one);
            index += width;
            taken = index;
        }

        carried.Append(Normalised(text[taken..]));

        return carried.ToString();
    }

    private static bool Normalises(string one)
    {
        try
        {
            one.Normalize(NormalizationForm.FormKC);

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
