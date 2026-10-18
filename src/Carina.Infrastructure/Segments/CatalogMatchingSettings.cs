namespace Carina.Infrastructure.Segments;

/// <summary>
/// How long after the start the records are first looked at for a recording to match against the
/// catalog, how often they are looked at, how soon after a match the next is looked for, and how far
/// before and after a recording the recordings it is matched against may have started.
/// </summary>
public sealed record CatalogMatchingSettings
{
    public static readonly CatalogMatchingSettings Default = new();

    public TimeSpan BeforeFirstLook { get; init; } = TimeSpan.FromSeconds(45);

    public TimeSpan BetweenLooks { get; init; } = TimeSpan.FromMinutes(1);

    public TimeSpan AfterAMatch { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan Around { get; init; } = TimeSpan.FromDays(60);
}
