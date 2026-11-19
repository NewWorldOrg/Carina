namespace Carina.Domain.Quality;

public readonly record struct WeighedReading(double Reading, long Weight);

public static class UsualReadings
{
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
