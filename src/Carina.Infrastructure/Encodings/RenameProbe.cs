namespace Carina.Infrastructure.Encodings;

public enum RenameStanding
{
    WouldBeARename = 1,

    WouldCrossAMount = 2,

    CannotWriteFrom = 3,

    CannotWriteTo = 4,
}

public sealed record RenameVerdict(RenameStanding Standing, string Note)
{
    public bool IsARename => Standing is RenameStanding.WouldBeARename;
}

/// <summary>
/// Asks whether moving something from one directory to another would be a rename, which is the
/// only kind of move that is all or nothing. Across two mounts the kernel refuses and the runtime
/// quietly copies instead, and a copy interrupted half way looks exactly like success (BR-ED2-009).
/// </summary>
public interface IRenameProbe
{
    RenameVerdict Probe(string from, string to);
}

public sealed class DirectoryRenameProbe : IRenameProbe
{
    public const int CrossDeviceLink = 18;

    private const string Token = ".carina-rename-probe-";

    public static RenameVerdict Read(IOException refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);

        return refusal.HResult is CrossDeviceLink
            ? new RenameVerdict(RenameStanding.WouldCrossAMount, refusal.Message)
            : new RenameVerdict(RenameStanding.CannotWriteTo, refusal.Message);
    }

    public RenameVerdict Probe(string from, string to)
    {
        ArgumentException.ThrowIfNullOrEmpty(from);
        ArgumentException.ThrowIfNullOrEmpty(to);

        if (!Directory.Exists(from))
        {
            return new RenameVerdict(RenameStanding.CannotWriteFrom, "there is no directory to write in");
        }

        string token = Token + Guid.NewGuid().ToString("N");
        string source = Path.Combine(from, token);
        string destination = Path.Combine(to, token);

        try
        {
            Directory.CreateDirectory(source);
        }
        catch (Exception refusal) when (refusal is IOException or UnauthorizedAccessException)
        {
            return new RenameVerdict(RenameStanding.CannotWriteFrom, refusal.Message);
        }

        if (string.Equals(Path.GetFullPath(from), Path.GetFullPath(to), StringComparison.Ordinal))
        {
            Remove(source);

            return new RenameVerdict(RenameStanding.WouldBeARename, string.Empty);
        }

        try
        {
            Directory.Move(source, destination);
        }
        catch (IOException refusal)
        {
            Remove(source);

            return Read(refusal);
        }
        catch (UnauthorizedAccessException refusal)
        {
            Remove(source);

            return new RenameVerdict(RenameStanding.CannotWriteTo, refusal.Message);
        }

        Remove(destination);

        return new RenameVerdict(RenameStanding.WouldBeARename, string.Empty);
    }

    private static void Remove(string probe)
    {
        try
        {
            Directory.Delete(probe);
        }
        catch (Exception gone) when (gone is IOException or UnauthorizedAccessException)
        {
            return;
        }
    }
}
