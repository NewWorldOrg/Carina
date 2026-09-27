namespace Carina.Domain.Programmes;

public enum GuideStanding
{
    NothingKnown = 1,

    Announced = 2,

    NoLongerAnnounced = 3,
}

/// <summary>
/// What the guide says about one broadcast: the row the EPG keeps, held against the mark a whole
/// reading of that service leaves on the programmes it named. A missing row says nothing while no
/// reading has ever heard the service whole; a missing row, or one with a mark older than the
/// service's newest whole reading, means the broadcast is no longer announced.
/// </summary>
public static class GuideReading
{
    public static GuideStanding Of(Programme? announced, DateTime? heardWholeAt)
    {
        if (announced is null)
        {
            return heardWholeAt is null ? GuideStanding.NothingKnown : GuideStanding.NoLongerAnnounced;
        }

        if (heardWholeAt is { } whole && announced.LastHeardAt is { } named && named < whole)
        {
            return GuideStanding.NoLongerAnnounced;
        }

        return announced.IsShadow ? GuideStanding.NothingKnown : GuideStanding.Announced;
    }
}
