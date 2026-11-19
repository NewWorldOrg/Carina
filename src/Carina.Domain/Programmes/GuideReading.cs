namespace Carina.Domain.Programmes;

public enum GuideStanding
{
    NothingKnown = 1,

    Announced = 2,

    NoLongerAnnounced = 3,
}

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
