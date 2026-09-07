namespace Carina.Domain.Migration;

public sealed record SourceRule
{
    public SourceRule(
        long id,
        string name,
        bool enabled,
        IReadOnlyList<ServiceKey> services,
        bool usesRegularExpression,
        bool caseSensitive,
        bool recordsAtATimeOfDay,
        bool boundsTheDuration,
        bool boundsThePeriod,
        bool namesItsOwnDestination,
        bool namesItsOwnEncodeSettings)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(services);

        foreach (ServiceKey service in services)
        {
            ArgumentNullException.ThrowIfNull(service);
        }

        Id = SourceRow.Of(id, nameof(id));
        Name = name;
        Enabled = enabled;
        Services = [.. services];
        UsesRegularExpression = usesRegularExpression;
        CaseSensitive = caseSensitive;
        RecordsAtATimeOfDay = recordsAtATimeOfDay;
        BoundsTheDuration = boundsTheDuration;
        BoundsThePeriod = boundsThePeriod;
        NamesItsOwnDestination = namesItsOwnDestination;
        NamesItsOwnEncodeSettings = namesItsOwnEncodeSettings;
    }

    public long Id { get; }

    public string Name { get; }

    public bool Enabled { get; }

    public IReadOnlyList<ServiceKey> Services { get; }

    public bool UsesRegularExpression { get; }

    public bool CaseSensitive { get; }

    public bool RecordsAtATimeOfDay { get; }

    public bool BoundsTheDuration { get; }

    public bool BoundsThePeriod { get; }

    public bool NamesItsOwnDestination { get; }

    public bool NamesItsOwnEncodeSettings { get; }
}
