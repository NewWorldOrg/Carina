using Carina.Domain.Quality;

namespace Carina.Api.Services;

public static class QualitySaying
{
    public static string NoSuchThreshold()
        => "A threshold is asked for by one of the keys this domain names: "
           + string.Join(", ", QualityThresholdShapes.Consulted.Select(shape => shape.Key))
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

    public static string NoSuchTrend()
        => $"A trend is asked for by a whole number of days from 1 to {QualityTrendFrame.MostDays}, read back from now, "
           + "and by one of the subjects this domain names: "
           + string.Join(", ", QualityTrendSubjects.All)
           + $"; asking for neither reads the last {QualityTrendFrame.ShippedDays} day of {QualityTrendSubject.PacketsLost}.";

    public static string NoSuchIncident()
        => "An anomaly is asked for by the identifier the ledger gave it, and nothing is kept under this one.";

    public static string AlreadySettled()
        => "This anomaly has been resolved, and acknowledging is for one that still stands; the same condition "
           + "coming back is kept as a new occurrence with its own acknowledgement.";

    public static string NotToldAboutYet()
        => "This anomaly has not been told about yet, and acknowledging follows being told; the next pass of the "
           + "supply watch tells about it.";

    public static string NoPassYet()
        => "The supply watch has not read anything yet, so there is nothing to say about whether the supplies are "
           + "being heard from.";
}
