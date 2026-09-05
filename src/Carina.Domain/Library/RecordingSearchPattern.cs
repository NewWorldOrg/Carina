using System.Text;

namespace Carina.Domain.Library;

public static class RecordingSearchPattern
{
    public const char EscapeLetter = '\\';

    public const string Escape = "\\";

    private const char Anything = '%';

    private const char OneLetter = '_';

    public static string Containing(string word)
    {
        ArgumentNullException.ThrowIfNull(word);

        StringBuilder carried = new(word.Length + 2);

        carried.Append(Anything);

        foreach (char letter in word)
        {
            if (letter is Anything or OneLetter or EscapeLetter)
            {
                carried.Append(EscapeLetter);
            }

            carried.Append(letter);
        }

        carried.Append(Anything);

        return carried.ToString();
    }
}
