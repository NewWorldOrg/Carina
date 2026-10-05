namespace Carina.Domain.Quality;

public readonly record struct WeighedReading(double Reading, long Weight);

public static class UsualReadings
{
    /// <summary>
    /// The first reading, counted from the worst, at which more than half of the weight has been passed, or null
    /// when nothing was read.
    /// </summary>
    public static double? Of(IEnumerable<WeighedReading> worstFirst)
    {
        ArgumentNullException.ThrowIfNull(worstFirst);

        WeighedReading[] ordered = [.. worstFirst];
        long whole = ordered.Sum(weighed => weighed.Weight);
        long passed = 0;

        foreach (WeighedReading weighed in ordered)
        {
            passed += weighed.Weight;

            if (passed * 2 > whole)
            {
                return weighed.Reading;
            }
        }

        return null;
    }
}
