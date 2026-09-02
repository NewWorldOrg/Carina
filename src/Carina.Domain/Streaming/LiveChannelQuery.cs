namespace Carina.Domain.Streaming;

public enum LiveChannelSort
{
    RemoteControlKey = 0,

    Name = 1,

    Viewers = 2,
}

public enum LiveChannelField
{
    Tuning = 1,

    Sessions = 2,
}

public sealed class LiveChannelQuery
{
    public const int MostPerPage = 200;

    public const int DefaultPerPage = 50;

    private LiveChannelQuery(
        LiveChannelSort sort,
        bool descending,
        IReadOnlyList<LiveChannelField> fields,
        int page,
        int perPage)
    {
        Sort = sort;
        Descending = descending;
        Fields = fields;
        Page = page;
        PerPage = perPage;
    }

    public LiveChannelSort Sort { get; }

    public bool Descending { get; }

    public IReadOnlyList<LiveChannelField> Fields { get; }

    public int Page { get; }

    public int PerPage { get; }

    public bool Asks(LiveChannelField field) => Fields.Contains(field);

    public static LiveChannelQuery? For(
        LiveChannelSort sort = LiveChannelSort.RemoteControlKey,
        bool descending = false,
        IReadOnlyList<LiveChannelField>? fields = null,
        int? page = null,
        int? perPage = null)
    {
        if (!Enum.IsDefined(sort) || page is < 1)
        {
            return null;
        }

        if (fields is not null && fields.Any(field => !Enum.IsDefined(field)))
        {
            return null;
        }

        return new LiveChannelQuery(
            sort,
            descending,
            fields is null ? [] : [.. fields.Distinct()],
            page ?? 1,
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
