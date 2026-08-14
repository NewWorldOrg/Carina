namespace Carina.Contracts;

internal static class WireName
{
    internal const int MaxLength = 64;

    internal const string Description =
        "1 to 64 characters of A-Z, a-z, 0-9, '-', '_' or '.'";

    internal static bool IsUsable(string? value)
    {
        if (value is null || value.Length is 0 or > MaxLength)
        {
            return false;
        }

        foreach (var c in value)
        {
            var allowed =
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
