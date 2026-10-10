namespace Carina.Infrastructure.DataBroadcast;

/// <summary>
/// What one pass over the ended recordings did with their data broadcast: how many it read and what became of
/// them, how many it left under a root out of reach, whether it stopped because something was being recorded,
/// watched or about to be recorded, and how many records it put back to be taken again.
/// </summary>
public sealed record DataBroadcastPass
{
    private DataBroadcastPass(
        bool alreadyRunning,
        bool nowhereToKeepThem,
        int read,
        int made,
        int missing,
        int failed,
        int outOfReach,
        bool yielded,
        int requeued)
    {
        AlreadyRunning = alreadyRunning;
        NowhereToKeepThem = nowhereToKeepThem;
        Read = read;
        Made = made;
        Missing = missing;
        Failed = failed;
        OutOfReach = outOfReach;
        Yielded = yielded;
        Requeued = requeued;
    }

    public bool AlreadyRunning { get; }

    public bool NowhereToKeepThem { get; }

    public int Read { get; }

    public int Made { get; }

    public int Missing { get; }

    public int Failed { get; }

    public int OutOfReach { get; }

    public bool Yielded { get; }

    /// <summary>
    /// How many records were put back to coming: failed with tries left, or made and no longer kept.
    /// </summary>
    public int Requeued { get; }

    public int Settled => Made + Missing + Failed;

    public int LeftForNextTime => Read - Settled;

    public static DataBroadcastPass Of(
        int read,
        int made,
        int missing,
        int failed,
        int outOfReach,
        bool yielded,
        int requeued)
    {
        Counted(read, nameof(read));
        Counted(made, nameof(made));
        Counted(missing, nameof(missing));
        Counted(failed, nameof(failed));
        Counted(outOfReach, nameof(outOfReach));
        Counted(requeued, nameof(requeued));

        if (made + missing + failed > read)
        {
            throw new ArgumentOutOfRangeException(
                nameof(read),
                read,
                $"A pass that read {read} recording(s) settled no more than that, not {made + missing + failed}.");
        }

        return new DataBroadcastPass(false, false, read, made, missing, failed, outOfReach, yielded, requeued);
    }

    public static DataBroadcastPass YieldedBeforeReadingAnything() => new(false, false, 0, 0, 0, 0, 0, true, 0);

    public static DataBroadcastPass RefusedBecauseOneIsRunning() => new(true, false, 0, 0, 0, 0, 0, false, 0);

    public static DataBroadcastPass RefusedBecauseThereIsNowhereToKeepThem() => new(false, true, 0, 0, 0, 0, 0, false, 0);

    private static void Counted(int counted, string name)
    {
        if (counted < 0)
        {
            throw new ArgumentOutOfRangeException(name, counted, "A pass counts what it did, never less than none.");
        }
    }
}
