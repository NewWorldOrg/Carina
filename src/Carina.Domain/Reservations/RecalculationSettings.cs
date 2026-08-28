namespace Carina.Domain.Reservations;

public sealed record RecalculationSettings
{
    public static readonly TimeSpan DefaultBeforeFirstPass = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan DefaultBetweenReconciliations = TimeSpan.FromMinutes(15);

    public TimeSpan BeforeFirstPass { get; init; } = DefaultBeforeFirstPass;

    public TimeSpan BetweenReconciliations { get; init; } = DefaultBetweenReconciliations;
}
