namespace Carina.Broadcast.Descriptors;

public static class LanguageCode
{
    public const int Size = 3;

    public static string Of(ReadOnlySpan<byte> code)
    {
        if (code.Length < Size)
        {
            return string.Empty;
        }

        Span<char> letters = stackalloc char[Size];

        for (int at = 0; at < Size; at++)
        {
            char letter = (char)code[at];

            if (!char.IsAsciiLetter(letter))
            {
                return string.Empty;
            }

            letters[at] = char.ToLowerInvariant(letter);
        }

        return new string(letters);
    }
}
