using System.Globalization;
using System.Text;

namespace Carina.Domain.Migration;

public static class MigrationNote
{
    public const int Longest = 512;

    public static string Of(string said)
    {
        ArgumentNullException.ThrowIfNull(said);

        StringBuilder kept = new(said.Length);

        foreach (char letter in said)
        {
            if (char.GetUnicodeCategory(letter) is UnicodeCategory.Control or UnicodeCategory.Format)
            {
                continue;
            }

            kept.Append(letter);
        }

        string trimmed = kept.ToString().Trim();

        return trimmed.Length <= Longest ? trimmed : trimmed[..Longest];
    }
}
