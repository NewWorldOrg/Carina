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

    private MigrationRuleConversion(string name, RuleQuery? query, MigrationRefusal? refusal)
    {
        Name = name;
        Query = query;
        Refusal = refusal;
    }

    public string Name { get; }

    public RuleQuery? Query { get; }

    public MigrationRefusal? Refusal { get; }

    public bool Expressible => Refusal is null;

    public static MigrationRuleConversion Of(SourceRule rule, IReadOnlySet<ServiceKey> inReach)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(inReach);

        SourceRuleTerms terms = rule.Terms;

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

        if (Wanted(terms) && terms.ExcludedFields != terms.Fields)
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

        if (Asked(terms, system) is not { } query)
        {
            return Cannot(rule, MigrationRefusal.Inexpressible);
        }

        return new MigrationRuleConversion(name, query, null);
    }

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

    private static RuleQuery? Asked(SourceRuleTerms terms, TuneSystem system)
    {
        List<string> said = [];
        string keyword = terms.Keyword.Trim();
        string excluded = terms.Excluded.Trim();

        if (keyword.Length > 0)
        {
            said.Add($"{Keyword}={Uri.EscapeDataString(keyword)}");
        }

        if (excluded.Length > 0)
        {
            said.Add($"{Exclude}={Uri.EscapeDataString(excluded)}");
        }

        if (Looking(terms) is not { } fields)
        {
            return null;
        }

        foreach (ProgrammeField field in fields)
        {
            said.Add($"{Fields}={field}");
        }

        foreach (SourceRuleGenre genre in terms.Genres)
        {
            if (genre.Genre > ProgrammeSearch.HighestGenre)
            {
                return null;
            }

            if (genre.SubGenre is not { } sort)
            {
                said.Add($"{Genre}={genre.Genre.ToString(CultureInfo.InvariantCulture)}");

                continue;
            }

            if (sort > ProgrammeSearch.HighestSubGenre)
            {
                return null;
            }

            said.Add(
                $"{SubGenre}={genre.Genre.ToString(CultureInfo.InvariantCulture)}"
                + $"-{sort.ToString(CultureInfo.InvariantCulture)}");
        }

        if (SourceWeek.Named(terms.Days) is not { } days)
        {
            return null;
        }

        foreach (DayOfWeek day in days)
        {
            said.Add($"{Day}={day}");
        }

        if (system is not TuneSystem.Unspecified)
        {
            said.Add($"{Type}={system}");
        }

        if (terms.Services.Count > ProgrammeSearch.MostChannels)
        {
            return null;
        }

        foreach (ServiceKey service in terms.Services)
        {
            said.Add($"{Channel}={service.Network.Value.ToString(CultureInfo.InvariantCulture)}"
                + $"-{service.Service.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        if (said.Count is 0)
        {
            return null;
        }

        string joined = string.Join('&', said);

        return joined.Length > RuleQuery.MaxLength ? null : new RuleQuery(joined);
    }

    private static IReadOnlyList<ProgrammeField>? Looking(SourceRuleTerms terms)
    {
        bool searching = terms.Keyword.Trim().Length > 0 || Wanted(terms);

        return (terms.Fields.HasFlag(SourceRuleFields.Title), terms.Fields.HasFlag(SourceRuleFields.Summary)) switch
        {
            (true, true) => [],
            (true, false) => [ProgrammeField.Title],
            (false, true) => [ProgrammeField.Description],
            _ => searching ? null : [],
        };
    }

    private static MigrationRuleConversion Cannot(SourceRule rule, MigrationRefusal refusal)
        => new(MigrationNote.Of(rule.Name), null, MigrationRefusals.Named(refusal));
}
