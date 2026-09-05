namespace Carina.Domain.Encodings;

/// <summary>
/// What a page of the job ledger is asked for: which standings, and which page of what size. A page
/// size over the ceiling is cut down to it and answered as the size that was used; a page below the
/// first is no page at all. Jobs come back newest first, so what is running and waiting is at the
/// top and what failed lately is next.
/// </summary>
public sealed class EncodeJobQuery
{
    public const int MostPerPage = 100;

    public const int DefaultPerPage = 20;

    private EncodeJobQuery(IReadOnlyList<EncodeJobStatus> statuses, int page, int perPage)
    {
        Statuses = statuses;
        Page = page;
        PerPage = perPage;
    }

    public IReadOnlyList<EncodeJobStatus> Statuses { get; }

    public int Page { get; }

    public int PerPage { get; }

    public static EncodeJobQuery? For(IReadOnlyList<EncodeJobStatus>? statuses, int? page, int? perPage)
    {
        if (page is < 1)
        {
            return null;
        }

        EncodeJobStatus[] asked = [.. (statuses ?? []).Distinct()];

        if (asked.Any(status => !Enum.IsDefined(status)))
        {
            return null;
        }

        int size = perPage switch
        {
            null or < 1 => DefaultPerPage,
            > MostPerPage => MostPerPage,
            _ => perPage.Value,
        };

        return new EncodeJobQuery(asked, page ?? 1, size);
    }
}
