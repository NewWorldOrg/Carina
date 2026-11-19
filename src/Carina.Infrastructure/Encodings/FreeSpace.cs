namespace Carina.Infrastructure.Encodings;

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
