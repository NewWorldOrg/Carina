using System.Globalization;

namespace Carina.Domain.Programmes;

public sealed record BulkCursor(int Generation, long Revision)
{
    public const int MostRows = 5_000;

    public const int DefaultRows = 1_000;

    public string Text => string.Create(CultureInfo.InvariantCulture, $"{Generation}:{Revision}");

    public static BulkCursor Beginning(int generation) => new(generation, 0);

    public static BulkCursor? Read(string? text)
    {
        string[] parts = (text ?? string.Empty).Split(':');

        if (parts.Length != 2)
        {
            return null;
        }

        if (!int.TryParse(parts[0], CultureInfo.InvariantCulture, out int generation)
            || !long.TryParse(parts[1], CultureInfo.InvariantCulture, out long revision))
        {
            return null;
        }

        return generation < 1 || revision < 0 ? null : new BulkCursor(generation, revision);
    }

    public static int Rows(int? asked)
        => asked switch
        {
            null or < 1 => DefaultRows,
            > MostRows => MostRows,
            { } wanted => wanted,
        };
}
