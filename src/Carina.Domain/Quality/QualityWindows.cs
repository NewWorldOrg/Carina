namespace Carina.Domain.Quality;

public static class QualityWindows
{
    public static readonly IReadOnlyList<QualityWindow> All = [QualityWindow.Minute, QualityWindow.Hour];

    public static TimeSpan Length(QualityWindow window) => window switch
    {
        QualityWindow.Minute => TimeSpan.FromMinutes(1),
        QualityWindow.Hour => TimeSpan.FromHours(1),
        _ => throw new ArgumentOutOfRangeException(nameof(window), window, "A window is one of the ones this domain keeps."),
    };

    public static DateTime StartOf(DateTime at, QualityWindow window)
    {
        long ticks = Length(window).Ticks;

        return new DateTime(at.Ticks - (at.Ticks % ticks), at.Kind);
    }

    public static DateTime EndOf(DateTime windowStart, QualityWindow window) => windowStart + Length(window);
}
