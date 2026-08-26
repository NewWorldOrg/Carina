using System.Globalization;

namespace Carina.Domain.Recordings;

public enum DiskShortfall
{
    RootsUnknown = 1,

    RootUndeclared = 2,

    RootUnmeasured = 3,

    RootNotWritable = 4,

    NoRoomLeft = 5,

    ShortOfTheEstimate = 6,
}

public sealed record DiskPrecheckVerdict
{
    private DiskPrecheckVerdict(DiskShortfall? shortfall, Int128 estimatedBytes, long freeBytes, int weighed)
    {
        Shortfall = shortfall;
        EstimatedBytes = estimatedBytes;
        FreeBytes = freeBytes;
        Weighed = weighed;
    }

    public DiskShortfall? Shortfall { get; }

    public Int128 EstimatedBytes { get; }

    public long FreeBytes { get; }

    public int Weighed { get; }

    public bool HasRoom => Shortfall is null;

    public static DiskPrecheckVerdict Of(
        DiskShortfall? shortfall,
        Int128 estimatedBytes,
        long freeBytes,
        int weighed)
    {
        if (shortfall is { } named && !Enum.IsDefined(named))
        {
            throw new ArgumentOutOfRangeException(
                nameof(shortfall),
                shortfall,
                "A precheck names what it found in the classes this one holds.");
        }

        if (estimatedBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(estimatedBytes),
                estimatedBytes,
                "A recording weighs nothing at the lightest.");
        }

        if (weighed < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(weighed),
                weighed,
                "A precheck weighs the recording that is starting, so it has looked at one recording at least.");
        }

        return new DiskPrecheckVerdict(shortfall, estimatedBytes, freeBytes, weighed);
    }

    public OutcomeDetail Detail(DateTime noticedAt)
        => Shortfall is { } shortfall
            ? new OutcomeDetail(RecordingFault.RefusedByDiskPrecheck, null, Note(shortfall), noticedAt)
            : throw new InvalidOperationException(
                "A precheck that found room has nothing to write down.");

    private string Note(DiskShortfall shortfall)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{shortfall}: {Weighed} recordings weigh {EstimatedBytes} bytes against {FreeBytes} free");
}
