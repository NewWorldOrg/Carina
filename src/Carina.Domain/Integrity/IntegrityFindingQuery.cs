using Carina.Domain.Base;

namespace Carina.Domain.Integrity;

public sealed class IntegrityFindingQuery
{
    public const int MostPerPage = 200;

    public const int DefaultPerPage = 50;

    private IntegrityFindingQuery(int page, int perPage)
    {
        Page = page;
        PerPage = perPage;
    }

    public int Page { get; }

    public int PerPage { get; }

    public static IntegrityFindingQuery? For(int? page, int? perPage)
        => page is < 1
            ? null
            : new IntegrityFindingQuery(page ?? 1, ListingGuards.Clamped(perPage, DefaultPerPage, MostPerPage));
}
