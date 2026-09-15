namespace Carina.Domain.Integrity;

public enum StrayErasureFault
{
    RootOutOfReach = 1,

    FileChanged = 2,

    BeingWritten = 3,

    FileLeftBehind = 4,

    DriverUnreachable = 5,

    DriverRefused = 6,
}

public sealed record StrayFileErasure
{
    private StrayFileErasure(StrayErasureFault? fault, string? note, StrayFileChange? change, bool fileRemoved)
    {
        if (fault is { } named && !Enum.IsDefined(named))
        {
            throw new ArgumentOutOfRangeException(nameof(fault), fault, "An erasure fails in one of six ways.");
        }

        if (fault is null != note is null)
        {
            throw new ArgumentException(
                "An erasure that failed says why, and one that did not says nothing.",
                nameof(note));
        }

        if (change is not null && fault is not StrayErasureFault.FileChanged)
        {
            throw new ArgumentException(
                "Only an erasure refused because the file changed says how it changed.",
                nameof(change));
        }

        Fault = fault;
        Note = note;
        Change = change;
        FileRemoved = fileRemoved;
    }

    public StrayErasureFault? Fault { get; }

    public string? Note { get; }

    public StrayFileChange? Change { get; }

    public bool FileRemoved { get; }

    public static StrayFileErasure Erased(bool fileRemoved) => new(null, null, null, fileRemoved);

    public static StrayFileErasure Refused(StrayErasureFault fault, string note, StrayFileChange? change = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(note);

        return new StrayFileErasure(fault, note, change, false);
    }
}

public interface IStrayFileEraser
{
    Task<StrayFileErasure> EraseAsync(IntegrityFinding finding, CancellationToken cancellationToken);
}
