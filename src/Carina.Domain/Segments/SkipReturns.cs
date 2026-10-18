namespace Carina.Domain.Segments;

/// <summary>
/// How many times viewers pressed a skip button one version's detection offered, over some days, and how
/// many times they came back soon after; the share that came back is not known without a skip.
/// </summary>
public sealed record SkipReturns
{
    public SkipReturns(int skips, int returned)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skips);
        ArgumentOutOfRangeException.ThrowIfNegative(returned);

        Skips = skips;
        Returned = returned;
    }

    public static SkipReturns None { get; } = new(0, 0);

    public int Skips { get; }

    public int Returned { get; }

    public double? Share => Skips is 0 ? null : Returned / (double)Skips;

    /// <summary>
    /// The skips and returns on the buttons of <paramref name="version"/> taken in from
    /// <paramref name="from"/> up to and including <paramref name="until"/>.
    /// </summary>
    public static SkipReturns Of(IEnumerable<ViewerAction> actions, int version, DateTime from, DateTime until)
    {
        ArgumentNullException.ThrowIfNull(actions);

        ViewerAction[] counted = [.. actions.Where(action => action.ModelVersion == version && action.HappenedAt >= from && action.HappenedAt <= until)];

        return new SkipReturns(
            counted.Count(action => action.Action is ViewerActionKind.Skipped),
            counted.Count(action => action.Action is ViewerActionKind.Returned));
    }
}
