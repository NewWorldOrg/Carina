using System.Globalization;

using Carina.Contracts;
using Carina.Domain.Programmes;
using Carina.Domain.Rules;

namespace Carina.Domain.Migration;

public sealed record MigrationRuleConversion
{
    public const string Keyword = "keyword";

    public const string Exclude = "exclude";

    public const string Fields = "fields";

    public const string Genre = "genre";

    public const string SubGenre = "subgenre";

    public const string Day = "day";

    public const string Type = "type";

    public const string Channel = "channel";

    public const string Mark = "mark";

    public const string ExcludeMark = "excludeMark";

    private MigrationRuleConversion(
        string name,
        RuleQuery? query,
        MigrationRefusal? refusal,
        bool narrowedByDay,
        bool markWordsReplaced)
    {
        Name = name;
        Query = query;
        Refusal = refusal;
        NarrowedByDay = narrowedByDay;
        MarkWordsReplaced = markWordsReplaced;
    }

    public string Name { get; }

    public RuleQuery? Query { get; }

    public MigrationRefusal? Refusal { get; }

    public bool Expressible => Refusal is null;

    public bool NarrowedByDay { get; }

    public bool MarkWordsReplaced { get; }

    public static MigrationRuleConversion Of(SourceRule rule, IReadOnlySet<ServiceKey> inReach)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(inReach);

        SourceRuleTerms terms = rule.Terms;
        SourceRuleWords keyword = SourceRuleWords.Read(terms.Keyword);
        SourceRuleWords excluded = SourceRuleWords.Read(terms.Excluded);

        if (terms.Services.Any(service => !inReach.Contains(service)))
        {
            return Cannot(rule, MigrationRefusal.Unidentifiable);
        }

        if (rule.Reach.UsesRegularExpression || rule.Reach.CaseSensitive)
        {
            return Cannot(rule, MigrationRefusal.Inexpressible);
        }

        if (rule.Reach.RecordsAtATimeOfDay
            || rule.Reach.BoundsTheDuration
            || rule.Reach.BoundsThePeriod
            || rule.Reach.NamesItsOwnDestination
            || rule.Reach.NamesItsOwnEncodeSettings
            || LooksAtTheExtendedBody(terms))
        {
            return Cannot(rule, MigrationRefusal.NoSuchFeature);
        }

        if (excluded.Words.Count > 0 && terms.ExcludedFields != terms.Fields)
        {
            return Cannot(rule, MigrationRefusal.Inexpressible);
        }

        if (SystemOf(terms.Kinds) is not { } system)
        {
            return Cannot(rule, MigrationRefusal.Inexpressible);
        }

        string name = MigrationNote.Of(rule.Name);

        if (name.Length is 0 || name.Length > Rule.NameMaxLength)
        {
            return Cannot(rule, MigrationRefusal.Inexpressible);
        }

        if (Asked(terms, system, keyword, excluded) is not { } query)
        {
            return Cannot(rule, MigrationRefusal.Inexpressible);
        }

        return new MigrationRuleConversion(
            name,
            query,
            null,
            Narrows(terms.Days),
            keyword.Marks.Count + excluded.Marks.Count > 0);
    }

    public static int RulesNarrowedByDay(SourceLedger ledger, IReadOnlySet<ServiceKey> inReach)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        int found = 0;

        foreach (SourceRule rule in ledger.Rules)
        {
            found += Of(rule, inReach).NarrowedByDay ? 1 : 0;
        }

        return found;
    }

    public static int RulesWithMarkWordsReplaced(SourceLedger ledger, IReadOnlySet<ServiceKey> inReach)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        return ledger.Rules.Count(rule => Of(rule, inReach).MarkWordsReplaced);
    }

    private static bool Narrows(int days) => SourceWeek.Named(days) is { Count: > 0 };

    private static bool LooksAtTheExtendedBody(SourceRuleTerms terms)
        => terms.Fields.HasFlag(SourceRuleFields.ExtendedBody)
            || (Wanted(terms) && terms.ExcludedFields.HasFlag(SourceRuleFields.ExtendedBody));

    private static bool Wanted(SourceRuleTerms terms) => terms.Excluded.Trim().Length > 0;

    private static TuneSystem? SystemOf(IReadOnlyList<SourceBroadcastKind> kinds)
        => kinds switch
        {
            [] => TuneSystem.Unspecified,
            [SourceBroadcastKind.Terrestrial] => TuneSystem.IsdbT,
            [SourceBroadcastKind.BroadcastSatellite] => TuneSystem.IsdbSBs,
            [SourceBroadcastKind.CommunicationSatellite] => TuneSystem.IsdbSCs110,
            _ => null,
        };

    private static RuleQuery? Asked(
        SourceRuleTerms terms,
        TuneSystem system,
        SourceRuleWords keyword,
        SourceRuleWords excluded)
    {
        if (Looking(terms, keyword, excluded) is not { } fields
            || SourceWeek.Named(terms.Days) is not { } days
            || terms.Services.Count > ProgrammeSearch.MostChannels)
        {
            return null;
        }

        ProgrammeConditions conditions = new()
        {
            Exclude = excluded.Spelt,
            Fields = fields,
            Genres = [.. terms.Genres.Where(genre => genre.SubGenre is null).Select(genre => genre.Genre)],
            SubGenres = [.. terms.Genres.SelectMany(Under)],
            Days = days,
            Marks = keyword.Marks,
            ExcludedMarks = excluded.Marks,
            System = system,
            Channels = [.. terms.Services.Select(service => new ProgrammeService(service.Network.Value, service.Service.Value))],
        };

        if (ProgrammeSearch.For(keyword.Spelt, null, null, conditions: conditions) is null)
        {
            return null;
        }

        string joined = string.Join('&', Said(terms, conditions, keyword));

        return joined.Length > RuleQuery.MaxLength ? null : new RuleQuery(joined);
    }

    private static IEnumerable<ProgrammeGenre> Under(SourceRuleGenre genre)
    {
        if (genre.SubGenre is { } sort)
        {
            yield return new ProgrammeGenre(genre.Genre, sort);
        }
    }

    private static IEnumerable<string> Said(SourceRuleTerms terms, ProgrammeConditions conditions, SourceRuleWords keyword)
    {
        if (keyword.Words.Count > 0)
        {
            yield return $"{Keyword}={Uri.EscapeDataString(keyword.Spelt)}";
        }

        if (!string.IsNullOrEmpty(conditions.Exclude))
        {
            yield return $"{Exclude}={Uri.EscapeDataString(conditions.Exclude)}";
        }

        foreach (ProgrammeField field in conditions.Fields ?? [])
        {
            yield return $"{Fields}={field}";
        }

        foreach (SourceRuleGenre genre in terms.Genres)
        {
            yield return Filed(genre);
        }

        foreach (DayOfWeek day in conditions.Days ?? [])
        {
            yield return $"{Day}={day}";
        }

        foreach (ProgrammeMark mark in conditions.Marks ?? [])
        {
            yield return $"{Mark}={mark}";
        }

        foreach (ProgrammeMark mark in conditions.ExcludedMarks ?? [])
        {
            yield return $"{ExcludeMark}={mark}";
        }

        if (conditions.System is { } system and not TuneSystem.Unspecified)
        {
            yield return $"{Type}={system}";
        }

        foreach (ProgrammeService service in conditions.Channels ?? [])
        {
            yield return string.Create(CultureInfo.InvariantCulture, $"{Channel}={service.NetworkId}-{service.ServiceId}");
        }
    }

    private static string Filed(SourceRuleGenre genre)
        => genre.SubGenre is { } sort
            ? string.Create(CultureInfo.InvariantCulture, $"{SubGenre}={genre.Genre}-{sort}")
            : string.Create(CultureInfo.InvariantCulture, $"{Genre}={genre.Genre}");

    private static IReadOnlyList<ProgrammeField>? Looking(
        SourceRuleTerms terms,
        SourceRuleWords keyword,
        SourceRuleWords excluded)
    {
        if (keyword.Words.Count is 0 && excluded.Words.Count is 0)
        {
            return [];
        }

        return (terms.Fields.HasFlag(SourceRuleFields.Title), terms.Fields.HasFlag(SourceRuleFields.Summary)) switch
        {
            (true, true) => [],
            (true, false) => [ProgrammeField.Title],
            (false, true) => [ProgrammeField.Description],
            _ => null,
        };
    }

    private static MigrationRuleConversion Cannot(SourceRule rule, MigrationRefusal refusal)
        => new(MigrationNote.Of(rule.Name), null, MigrationRefusals.Named(refusal), false, false);
}
