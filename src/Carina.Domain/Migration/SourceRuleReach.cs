namespace Carina.Domain.Migration;

public sealed record SourceRuleReach(
    bool UsesRegularExpression,
    bool CaseSensitive,
    bool RecordsAtATimeOfDay,
    bool BoundsTheDuration,
    bool BoundsThePeriod,
    bool NamesItsOwnDestination,
    bool NamesItsOwnEncodeSettings)
{
    public static SourceRuleReach Plain { get; } = new(false, false, false, false, false, false, false);
}
