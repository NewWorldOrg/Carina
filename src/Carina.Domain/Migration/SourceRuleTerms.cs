namespace Carina.Domain.Migration;

public sealed record SourceRuleTerms
{
    public SourceRuleTerms(
        string keyword,
        string excluded,
        SourceRuleFields fields,
        SourceRuleFields excludedFields,
        IReadOnlyList<SourceBroadcastKind> kinds,
        IReadOnlyList<ServiceKey> services,
        IReadOnlyList<SourceRuleGenre> genres,
        int days)
    {
        ArgumentNullException.ThrowIfNull(keyword);
        ArgumentNullException.ThrowIfNull(excluded);
        ArgumentNullException.ThrowIfNull(kinds);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(genres);

        foreach (SourceBroadcastKind kind in kinds)
        {
            if (!Enum.IsDefined(kind))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(kinds),
                    kind,
                    "A broadcast of the source system is one it can name.");
            }
        }

        foreach (ServiceKey service in services)
        {
            ArgumentNullException.ThrowIfNull(service, nameof(services));
        }

        foreach (SourceRuleGenre genre in genres)
        {
            ArgumentNullException.ThrowIfNull(genre, nameof(genres));
        }

        Keyword = keyword;
        Excluded = excluded;
        Fields = SourceRuleFieldsRead.Of(fields, nameof(fields));
        ExcludedFields = SourceRuleFieldsRead.Of(excludedFields, nameof(excludedFields));
        Kinds = [.. kinds.Distinct().Order()];
        Services = [.. services];
        Genres = [.. genres];
        Days = SourceWeek.Of(days, nameof(days));
    }

    public string Keyword { get; }

    public string Excluded { get; }

    public SourceRuleFields Fields { get; }

    public SourceRuleFields ExcludedFields { get; }

    public IReadOnlyList<SourceBroadcastKind> Kinds { get; }

    public IReadOnlyList<ServiceKey> Services { get; }

    public IReadOnlyList<SourceRuleGenre> Genres { get; }

    public int Days { get; }

    public static SourceRuleTerms Of(string keyword, params ServiceKey[] services)
        => new(
            keyword,
            string.Empty,
            SourceRuleFields.Title | SourceRuleFields.Summary,
            SourceRuleFields.Title | SourceRuleFields.Summary,
            [],
            services,
            [],
            SourceWeek.EveryDay);
}
