namespace Carina.Domain.Segments;

public static class SegmentGrid
{
    public const int MostSteps = 12;

    public static TimeSpan Step => CatalogKinds.Grid;

    public static TimeSpan Longest => Step * MostSteps;

    public static TimeSpan Off(TimeSpan length)
    {
        TimeSpan over = TimeSpan.FromTicks(Math.Abs(length.Ticks) % Step.Ticks);

        return over <= Step - over ? over : Step - over;
    }

    public static TimeSpan Nearest(TimeSpan shortest, TimeSpan longest)
    {
        if (longest < shortest)
        {
            throw new ArgumentException("The longest a length may be is no shorter than the shortest.", nameof(longest));
        }

        TimeSpan inside = Step * Math.Ceiling(shortest / Step);

        if (inside <= longest)
        {
            return inside;
        }

        return Off(shortest) <= Off(longest) ? shortest : longest;
    }

    public static bool IsAMultiple(TimeSpan shortest, TimeSpan longest, TimeSpan tolerance)
    {
        TimeSpan from = shortest > Step - tolerance ? shortest : Step - tolerance;
        TimeSpan to = longest < Longest + tolerance ? longest : Longest + tolerance;

        return from <= to && Off(Nearest(from, to)) <= tolerance;
    }

    public static bool IsAsLongAsAnAdvert(TimeSpan shortest, TimeSpan longest, TimeSpan tolerance)
        => IsAMultiple(shortest, longest, tolerance)
           && Math.Round(Nearest(shortest, longest) / Step, MidpointRounding.AwayFromZero) <= SegmentFeatures.LongestAdvertSteps;
}
