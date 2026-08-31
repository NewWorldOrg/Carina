namespace Carina.Domain.Recordings;

public enum ErasureFault
{
    RootOutOfReach = 1,

    FileLeftBehind = 2,

    DriverUnreachable = 3,

    DriverRefused = 4,
}

public sealed record RecordingErasure
{
    private RecordingErasure(ErasureFault? fault, string? note, int filesRemoved)
    {
        if (fault is { } named && !Enum.IsDefined(named))
        {
            throw new ArgumentOutOfRangeException(nameof(fault), fault, "An erasure fails in one of four ways.");
        }

        if (fault is null != note is null)
        {
            throw new ArgumentException(
                "An erasure that failed says why, and one that did not says nothing.",
                nameof(note));
        }

        if (filesRemoved < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(filesRemoved),
                filesRemoved,
                "An erasure removes what it removed, never less than none.");
        }

        Fault = fault;
        Note = note;
        FilesRemoved = filesRemoved;
    }

    public ErasureFault? Fault { get; }

    public string? Note { get; }

    public int FilesRemoved { get; }

    public bool EverythingIsGone => Fault is null;

    public static RecordingErasure Erased(int filesRemoved) => new(null, null, filesRemoved);

    public static RecordingErasure Refused(ErasureFault fault, string note)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(note);

        return new RecordingErasure(fault, note, 0);
    }
}

public interface IRecordingFileEraser
{
    Task<RecordingErasure> EraseAsync(
        RecordingId id,
        OutputRoot root,
        CancellationToken cancellationToken);
}
