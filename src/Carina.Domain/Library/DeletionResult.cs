namespace Carina.Domain.Library;

public enum DeletionRefusal
{
    NotFound = 1,

    StillRecording = 2,

    RootUnavailable = 3,

    PartialFailure = 4,
}

public sealed record DeletionResult
{
    private DeletionResult(DeletionRefusal? refusal, IReadOnlyList<string> leftBehind)
    {
        Refusal = refusal;
        LeftBehind = leftBehind;
    }

    public DeletionRefusal? Refusal { get; }

    public IReadOnlyList<string> LeftBehind { get; }

    public bool Deleted => Refusal is null;

    public static DeletionResult Done() => new(null, []);

    public static DeletionResult Refused(DeletionRefusal refusal, IReadOnlyList<string>? leftBehind = null)
    {
        if (!Enum.IsDefined(refusal))
        {
            throw new ArgumentOutOfRangeException(
                nameof(refusal),
                refusal,
                "A deletion that did not happen names one of the reasons this type holds.");
        }

        if (refusal is DeletionRefusal.PartialFailure && (leftBehind is null || leftBehind.Count is 0))
        {
            throw new ArgumentException(
                "A deletion that got part of the way names the files it could not remove, because the row stays until they are gone.",
                nameof(leftBehind));
        }

        if (refusal is not DeletionRefusal.PartialFailure && leftBehind is { Count: > 0 })
        {
            throw new ArgumentException(
                $"A deletion refused as {refusal} never reached a file, so it leaves none behind.",
                nameof(leftBehind));
        }

        return new DeletionResult(refusal, [.. leftBehind ?? []]);
    }
}
