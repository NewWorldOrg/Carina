using Carina.Domain.Programmes;

namespace Carina.Domain.Reservations;

internal static class ListingGuards
{
    public const int MostPerPage = 200;

    public const int DefaultPerPage = 50;

    public const int MostChannels = 64;

    public static readonly TimeSpan LongestSpan = TimeSpan.FromDays(366);

    public static int Clamped(int? perPage)
        => perPage switch
        {
            null or < 1 => DefaultPerPage,
            > MostPerPage => MostPerPage,
            { } asked => asked,
        };

    public static bool SpanIsUnusable(DateTime? from, DateTime? to)
    {
        if (from is { } start && start.Kind is not DateTimeKind.Utc)
        {
            return true;
        }

        if (to is { } end && end.Kind is not DateTimeKind.Utc)
        {
            return true;
        }

        return from is { } began && to is { } finished && (finished <= began || finished - began > LongestSpan);
    }

    public static IReadOnlyList<ProgrammeService>? ChannelsIn(IReadOnlyList<ProgrammeService>? asked)
    {
        if (asked is null || asked.Count == 0)
        {
            return [];
        }

        ProgrammeService[] apart = [.. asked.Distinct()];

        return apart.Length > MostChannels ? null : apart;
    }

    public static IReadOnlyList<T>? NamedIn<T>(IReadOnlyList<T>? asked)
        where T : struct, Enum
    {
        if (asked is null || asked.Count == 0)
        {
            return [];
        }

        return asked.Any(named => !Enum.IsDefined(named)) ? null : [.. asked.Distinct()];
    }
}
