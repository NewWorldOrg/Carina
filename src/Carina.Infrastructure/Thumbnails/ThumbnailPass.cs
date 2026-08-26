namespace Carina.Infrastructure.Thumbnails;

public sealed record ThumbnailPass
{
    private ThumbnailPass(
        bool alreadyRunning,
        bool nowhereToPutThem,
        int read,
        int drawn,
        int skipped,
        int failed,
        int outOfReach)
    {
        AlreadyRunning = alreadyRunning;
        NowhereToPutThem = nowhereToPutThem;
        Read = read;
        Drawn = drawn;
        Skipped = skipped;
        Failed = failed;
        OutOfReach = outOfReach;
    }

    public bool AlreadyRunning { get; }

    public bool NowhereToPutThem { get; }

    public int Read { get; }

    public int Drawn { get; }

    public int Skipped { get; }

    public int Failed { get; }

    public int OutOfReach { get; }

    public int LeftForNextTime => Read - Drawn - Skipped - Failed;

    public static ThumbnailPass Of(int read, int drawn, int skipped, int failed, int outOfReach)
    {
        Counted(read, nameof(read));
        Counted(drawn, nameof(drawn));
        Counted(skipped, nameof(skipped));
        Counted(failed, nameof(failed));
        Counted(outOfReach, nameof(outOfReach));

        if (drawn + skipped + failed > read)
        {
            throw new ArgumentOutOfRangeException(
                nameof(read),
                read,
                $"A pass that read {read} recording(s) settled no more than that, not {drawn + skipped + failed}.");
        }

        return new ThumbnailPass(false, false, read, drawn, skipped, failed, outOfReach);
    }

    public static ThumbnailPass RefusedBecauseOneIsRunning() => new(true, false, 0, 0, 0, 0, 0);

    public static ThumbnailPass RefusedBecauseThereIsNowhereToPutThem() => new(false, true, 0, 0, 0, 0, 0);

    private static void Counted(int counted, string name)
    {
        if (counted < 0)
        {
            throw new ArgumentOutOfRangeException(name, counted, "A pass counts what it did, never less than none.");
        }
    }
}
