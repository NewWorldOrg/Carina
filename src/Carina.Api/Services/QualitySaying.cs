using Carina.Domain.Quality;

namespace Carina.Api.Services;

public static class QualitySaying
{
    public static string NoSuchThreshold()
        => "A threshold is asked for by one of the keys this domain names: "
           + string.Join(", ", QualityThresholdShapes.All.Select(shape => shape.Key))
           + ".";

    public static string OutOfRange(QualityThresholdShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);

        return $"The level kept under {shape.Key} lies between {shape.Lowest} and {shape.Highest}, "
               + "and a level outside that is refused rather than saved and warned about.";
    }

    public static string OutOfOrder(QualityThresholdKey key)
        => $"A reading passes the warning level before it passes the unwatchable one, so {key} cannot be moved "
           + "past the level beside it.";

    public static string NoSuchPeriod()
        => "A period is asked for by two UTC instants, the second after the first and no further apart than "
           + $"{QualityPeriod.LongestSpan.TotalDays} days; asking for neither reads the last "
           + $"{QualityPeriod.ShippedSpan.TotalHours} hours.";

    public static string NoSuchPage(int mostPerPage)
        => $"A page is asked for by a page number of at least 1, a page size above {mostPerPage} is cut down to it "
           + "and answered as the size that was used, and each measure and ordering is one this domain names.";
}
