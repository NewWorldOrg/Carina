namespace Carina.Domain.Base;

public sealed class PaginatedList<T>
{
    public PaginatedList(IReadOnlyList<T> items, int total, int currentPage, int perPage)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfLessThan(currentPage, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(perPage, 1);

        Items = items;
        Total = total;
        CurrentPage = currentPage;
        PerPage = perPage;
        LastPage = total > 0 ? (int)Math.Ceiling((double)total / perPage) : 1;
    }

    public IReadOnlyList<T> Items { get; }

    public int Total { get; }

    public int CurrentPage { get; }

    public int LastPage { get; }

    public int PerPage { get; }
}
