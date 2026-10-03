namespace Carina.Infrastructure.Captions;

public sealed record CaptionPass
{
    private CaptionPass(
        bool alreadyRunning,
        bool nowhereToKeepThem,
        int read,
        int kept,
        int absent,
        int failed,
        int outOfReach,
        bool yielded,
        int requeued)
    {
        AlreadyRunning = alreadyRunning;
        NowhereToKeepThem = nowhereToKeepThem;
        Read = read;
        Kept = kept;
        Absent = absent;
        Failed = failed;
        OutOfReach = outOfReach;
        Yielded = yielded;
        Requeued = requeued;
    }

    public bool AlreadyRunning { get; }

    public bool NowhereToKeepThem { get; }

    public int Read { get; }

    public int Kept { get; }

    public int Absent { get; }

    public int Failed { get; }

    public int OutOfReach { get; }

    /// <summary>
    /// Whether the pass stopped early because something was being recorded or watched.
    /// </summary>
    public bool Yielded { get; }

    /// <summary>
    /// How many recordings said their captions were ready with no record of them kept, and were put back.
    /// </summary>
    public int Requeued { get; }

    public int Settled => Kept + Absent + Failed;

    public int LeftForNextTime => Read - Settled;

    public static CaptionPass Of(int read, int kept, int absent, int failed, int outOfReach, bool yielded, int requeued = 0)
    {
        Counted(read, nameof(read));
        Counted(kept, nameof(kept));
        Counted(absent, nameof(absent));
        Counted(failed, nameof(failed));
        Counted(outOfReach, nameof(outOfReach));
        Counted(requeued, nameof(requeued));

        if (kept + absent + failed > read)
        {
            throw new ArgumentOutOfRangeException(
                nameof(read),
                read,
                $"A pass that read {read} recording(s) settled no more than that, not {kept + absent + failed}.");
        }

        return new CaptionPass(false, false, read, kept, absent, failed, outOfReach, yielded, requeued);
    }

    public static CaptionPass RefusedBecauseOneIsRunning() => new(true, false, 0, 0, 0, 0, 0, false, 0);

    public static CaptionPass RefusedBecauseThereIsNowhereToKeepThem() => new(false, true, 0, 0, 0, 0, 0, false, 0);

    private static void Counted(int counted, string name)
    {
        if (counted < 0)
        {
            throw new ArgumentOutOfRangeException(name, counted, "A pass counts what it did, never less than none.");
        }
    }
}
