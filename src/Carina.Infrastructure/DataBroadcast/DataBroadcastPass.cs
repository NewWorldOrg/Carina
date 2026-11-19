namespace Carina.Infrastructure.DataBroadcast;

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

    public static DataBroadcastPass YieldedBeforeReadingAnything(int requeued)
    {
        Counted(requeued, nameof(requeued));

        return new DataBroadcastPass(false, false, 0, 0, 0, 0, 0, true, requeued);
    }

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
