namespace Carina.Infrastructure.Encodings;

/// <summary>
/// How much room is left where a directory lies, or null when it cannot be told.
/// </summary>
public interface IFreeSpaceReader
{
    long? FreeBytes(string directory);
}

public sealed class DriveFreeSpace : IFreeSpaceReader
{
    public long? FreeBytes(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);

        try
        {
            return new DriveInfo(directory).AvailableFreeSpace;
        }
        catch (Exception unmeasured) when (unmeasured is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
