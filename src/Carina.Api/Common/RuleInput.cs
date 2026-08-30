using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Infrastructure.Programmes;

namespace Carina.Api.Common;

public enum RuleInputFault
{
    None = 0,

    NameIsMissing = 1,

    NameIsTooLong = 2,

    QueryIsMissing = 3,

    QueryIsTooLong = 4,

    QueryIsNotAQueryString = 5,

    QueryNarrowsNothing = 6,

    PriorityOutOfRange = 7,

    MarginOutOfRange = 8,
}

public static class RuleInput
{
    public static string Because(RuleInputFault fault) => fault switch
    {
        RuleInputFault.NameIsMissing => "name: a rule is named so a person can find it again, so the name is asked "
            + "for rather than left to the query.",
        RuleInputFault.NameIsTooLong =>
            $"name: a rule name is at most {Rule.NameMaxLength} characters.",
        RuleInputFault.QueryIsMissing => "query: a rule carries the query string a programme search carries, "
            + "without its leading question mark.",
        RuleInputFault.QueryIsTooLong => $"query: a rule query is at most {RuleQuery.MaxLength} characters.",
        RuleInputFault.QueryIsNotAQueryString => "query: a rule query is a sequence of named parameters, without "
            + "a leading question mark and without a fragment.",
        RuleInputFault.QueryNarrowsNothing => "query: the search reads this as narrowing nothing, either because "
            + "every condition is empty or because a value it does not take is among them. A rule whose "
            + "conditions are all empty takes the whole guide, so it is refused rather than saved.",
        RuleInputFault.PriorityOutOfRange =>
            $"priority: a priority is {Priority.MinValue} to {Priority.MaxValue}.",
        RuleInputFault.MarginOutOfRange =>
            $"margin: a margin is a whole number of seconds from 0 to {ReservationInput.LongestMarginSeconds}.",
        _ => Description,
    };

    public static string Description
        => $"A rule carries a name of 1 to {Rule.NameMaxLength} characters and the query string a programme "
            + $"search carries, of at most {RuleQuery.MaxLength} characters, without its leading question mark. "
            + "A rule whose conditions are all empty narrows nothing and is refused rather than saved as one "
            + $"that takes everything. A priority is {Priority.MinValue} to {Priority.MaxValue}, and a margin "
            + $"is a whole number of seconds from 0 to {ReservationInput.LongestMarginSeconds}.";

    public static RuleInputFault NameFault(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return RuleInputFault.NameIsMissing;
        }

        return name.Length > Rule.NameMaxLength ? RuleInputFault.NameIsTooLong : RuleInputFault.None;
    }

    public static RuleInputFault DraftFault(string? query, int? priority, int? before, int? after)
    {
        if (priority is { } asked && (asked < Priority.MinValue || asked > Priority.MaxValue))
        {
            return RuleInputFault.PriorityOutOfRange;
        }

        if (!ReservationInput.Holds(null, before, after))
        {
            return RuleInputFault.MarginOutOfRange;
        }

        return QueryFault(query);
    }

    public static RuleInputFault QueryFault(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return RuleInputFault.QueryIsMissing;
        }

        if (query.Length > RuleQuery.MaxLength)
        {
            return RuleInputFault.QueryIsTooLong;
        }

        if (!IsAQueryString(query))
        {
            return RuleInputFault.QueryIsNotAQueryString;
        }

        return ProgrammeSearchQuery.Read(query) is null
            ? RuleInputFault.QueryNarrowsNothing
            : RuleInputFault.None;
    }

    private static bool IsAQueryString(string query)
    {
        try
        {
            _ = new RuleQuery(query);

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
