namespace Carina.Domain.Base;

internal static class ListingGuards
{
    public static int Clamped(int? perPage, int unasked, int most)
        => perPage switch
        {
            null or < 1 => unasked,
            { } asked when asked > most => most,
            { } asked => asked,
        };

    public static bool SpanIsUnusable(DateTime? from, DateTime? to, TimeSpan longest)
    {
        if (from is { Kind: not DateTimeKind.Utc } || to is { Kind: not DateTimeKind.Utc })
        {
            return true;
        }

        return from is { } began && to is { } finished && (finished <= began || finished - began > longest);
    }

    public static IReadOnlyList<T>? NoMoreThan<T>(IReadOnlyList<T>? asked, int most)
    {
        if (asked is null || asked.Count is 0)
        {
            return [];
        }

        T[] apart = [.. asked.Distinct()];

        return apart.Length > most ? null : apart;
    }

    public static IReadOnlyList<T>? NamedIn<T>(IReadOnlyList<T>? asked)
        where T : struct, Enum
    {
        if (asked is null || asked.Count is 0)
        {
            return [];
        }

        return asked.Any(named => !Enum.IsDefined(named)) ? null : [.. asked.Distinct()];
    }
}
