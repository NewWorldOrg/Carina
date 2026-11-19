namespace Carina.Domain.Segments;

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

    public static SkipReturns Of(IEnumerable<ViewerAction> actions, int version, DateTime from, DateTime until)
    {
        ArgumentNullException.ThrowIfNull(actions);

        ViewerAction[] counted = [.. actions.Where(action => action.ModelVersion == version && action.HappenedAt >= from && action.HappenedAt <= until)];

        return new SkipReturns(
            counted.Count(action => action.Action is ViewerActionKind.Skipped),
            counted.Count(action => action.Action is ViewerActionKind.Returned));
    }
}
