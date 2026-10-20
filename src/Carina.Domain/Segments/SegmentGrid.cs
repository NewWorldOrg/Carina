namespace Carina.Domain.Segments;

/// <summary>
/// The 15-second grid adverts are laid on. A length is a multiple of it when it lies within the tolerance
/// of a whole number of steps from one to <see cref="MostSteps"/>, on either side of it.
/// </summary>
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

    /// <summary>
    /// The length from <paramref name="shortest"/> to <paramref name="longest"/> nearest a whole number
    /// of steps: the first such number inside, or the nearer end.
    /// </summary>
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

    /// <summary>
    /// Whether a length from <paramref name="shortest"/> to <paramref name="longest"/> is as long as an advert lasts: a
    /// multiple of the grid of at most <see cref="SegmentFeatures.LongestAdvertSteps"/> steps.
    /// </summary>
    public static bool IsAsLongAsAnAdvert(TimeSpan shortest, TimeSpan longest, TimeSpan tolerance)
        => IsAMultiple(shortest, longest, tolerance)
           && Math.Round(Nearest(shortest, longest) / Step, MidpointRounding.AwayFromZero) <= SegmentFeatures.LongestAdvertSteps;
}
