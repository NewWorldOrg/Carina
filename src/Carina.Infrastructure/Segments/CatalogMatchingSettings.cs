namespace Carina.Infrastructure.Segments;

public sealed record CatalogMatchingSettings
{
    public static readonly CatalogMatchingSettings Default = new();

    public TimeSpan BeforeFirstLook { get; init; } = TimeSpan.FromSeconds(45);

    public TimeSpan BetweenLooks { get; init; } = TimeSpan.FromMinutes(1);

    public TimeSpan AfterAMatch { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan Around { get; init; } = TimeSpan.FromDays(60);
}
