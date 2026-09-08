namespace Carina.Domain.Quality;

public sealed record QualitySignalSettings
{
    public TimeSpan BeforeFirstSample { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan BetweenSamples { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan BeforeFirstRollup { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan BetweenRollups { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan KeepSamplesFor { get; init; } = TimeSpan.FromDays(7);

    public IReadOnlyDictionary<QualityWindow, TimeSpan?> KeepWindowsFor { get; init; } =
        new Dictionary<QualityWindow, TimeSpan?>
        {
            [QualityWindow.Minute] = TimeSpan.FromDays(90),
            [QualityWindow.Hour] = null,
        };

    public TimeSpan? KeptFor(QualityWindow window)
        => KeepWindowsFor.TryGetValue(window, out TimeSpan? kept) ? kept : null;
}
