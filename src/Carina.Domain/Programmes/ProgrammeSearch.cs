using Carina.Domain.Base;

namespace Carina.Domain.Programmes;

public enum ProgrammeSort
{
    StartsAt = 0,

    Name = 1,
}

public sealed class ProgrammeSearch
{
    public const int ShortestKeyword = 2;

    public const int MostPerPage = 200;

    public const int DefaultPerPage = 50;

    public static readonly TimeSpan LongestSpan = TimeSpan.FromDays(31);

    private ProgrammeSearch(
        string keyword,
        DateTime? from,
        DateTime? to,
        ProgrammeSort sort,
        bool descending,
        int page,
        int perPage)
    {
        Keyword = keyword;
        From = from;
        To = to;
        Sort = sort;
        Descending = descending;
        Page = page;
        PerPage = perPage;
    }

    public string Keyword { get; }

    public DateTime? From { get; }

    public DateTime? To { get; }

    public ProgrammeSort Sort { get; }

    public bool Descending { get; }

    public int Page { get; }

    public int PerPage { get; }

    public static ProgrammeSearch? For(
        string? keyword,
        DateTime? from,
        DateTime? to,
        ProgrammeSort sort = ProgrammeSort.StartsAt,
        bool descending = false,
        int? page = null,
        int? perPage = null)
    {
        string asked = (keyword ?? string.Empty).Trim();

        if (asked.Length < ShortestKeyword)
        {
            return null;
        }

        if (from is { } start && start.Kind is not DateTimeKind.Utc)
        {
            return null;
        }

        if (to is { } end && end.Kind is not DateTimeKind.Utc)
        {
            return null;
        }

        if (from is { } began && to is { } finished && (finished <= began || finished - began > LongestSpan))
        {
            return null;
        }

        return new ProgrammeSearch(
            asked,
            from,
            to,
            sort,
            descending,
            page is { } asking && asking > 1 ? asking : 1,
            Clamped(perPage));
    }

    private static int Clamped(int? perPage)
        => perPage switch
        {
            null or < 1 => DefaultPerPage,
            > MostPerPage => MostPerPage,
            { } asked => asked,
        };
}
