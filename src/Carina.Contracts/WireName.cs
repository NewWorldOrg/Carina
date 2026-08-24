namespace Carina.Contracts;

public static class WireName
{
    public static readonly int MaxLength = 64;

    public static readonly string Description =
        "1 to 64 characters of A-Z, a-z, 0-9, '-', '_' or '.'";

    public static bool IsUsable(string? value)
    {
        if (value is null || value.Length is 0 || value.Length > MaxLength)
        {
            return false;
        }

        foreach (char c in value)
        {
            bool allowed =
                c is >= 'a' and <= 'z'
                    or >= 'A' and <= 'Z'
                    or >= '0' and <= '9'
                    or '-'
                    or '_'
                    or '.';
            if (!allowed)
            {
                return false;
            }
        }

        return !value.Contains("..", StringComparison.Ordinal);
    }
}
