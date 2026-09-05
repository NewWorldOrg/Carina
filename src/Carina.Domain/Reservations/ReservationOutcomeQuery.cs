using Carina.Domain.Programmes;
using Carina.Domain.Rules;

namespace Carina.Domain.Reservations;

public sealed record ReservationOutcomeConditions
{
    public IReadOnlyList<ReservationOutcomeKind>? Kinds { get; init; }

    public IReadOnlyList<ProgrammeService>? Channels { get; init; }

    public RuleId? Rule { get; init; }
}

/// <summary>
/// What the ledger is asked for: a span of when the outcomes were written down, narrowed by
/// classification, channel and the rule the reservation came of. Newest first is the only order
/// there is, because the ledger is read as a history.
/// </summary>
public sealed class ReservationOutcomeQuery
{
    public const int MostPerPage = ListingGuards.MostPerPage;

    public const int DefaultPerPage = ListingGuards.DefaultPerPage;

    public const int MostChannels = ListingGuards.MostChannels;

    public static readonly TimeSpan LongestSpan = ListingGuards.LongestSpan;

    private ReservationOutcomeQuery(
        IReadOnlyList<ReservationOutcomeKind> kinds,
        IReadOnlyList<ProgrammeService> channels,
        RuleId? rule,
        DateTime? from,
        DateTime? to,
        int page,
        int perPage)
    {
        Kinds = kinds;
        Channels = channels;
        Rule = rule;
        From = from;
        To = to;
        Page = page;
        PerPage = perPage;
    }

    public IReadOnlyList<ReservationOutcomeKind> Kinds { get; }

    public IReadOnlyList<ProgrammeService> Channels { get; }

    public RuleId? Rule { get; }

    public DateTime? From { get; }

    public DateTime? To { get; }

    public int Page { get; }

    public int PerPage { get; }

    public static ReservationOutcomeQuery? For(
        DateTime? from,
        DateTime? to,
        int? page = null,
        int? perPage = null,
        ReservationOutcomeConditions? conditions = null)
    {
        ReservationOutcomeConditions beside = conditions ?? new ReservationOutcomeConditions();

        if (ListingGuards.NamedIn(beside.Kinds) is not { } kinds
            || ListingGuards.ChannelsIn(beside.Channels) is not { } channels)
        {
            return null;
        }

        if (ListingGuards.SpanIsUnusable(from, to) || page is < 1)
        {
            return null;
        }

        return new ReservationOutcomeQuery(
            kinds,
            channels,
            beside.Rule,
            from,
            to,
            page ?? 1,
            ListingGuards.Clamped(perPage));
    }
}
