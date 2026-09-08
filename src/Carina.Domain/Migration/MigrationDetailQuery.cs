namespace Carina.Domain.Migration;

public sealed class MigrationDetailQuery
{
    public const int MostPerPage = 500;

    public const int DefaultPerPage = 200;

    private MigrationDetailQuery(int page, int perPage)
    {
        Page = page;
        PerPage = perPage;
    }

    public int Page { get; }

    public int PerPage { get; }

    public static MigrationDetailQuery? For(int? page, int? perPage)
        => page is < 1 ? null : new MigrationDetailQuery(page ?? 1, Clamped(perPage));

    private static int Clamped(int? perPage)
        => perPage switch
        {
            null or < 1 => DefaultPerPage,
            > MostPerPage => MostPerPage,
            { } asked => asked,
        };
}
