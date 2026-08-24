using Carina.Contracts;
using Carina.Driver.Configuration;

namespace Carina.Driver.Ipc;

public static class StorageViews
{
    public const string WriteProbePrefix = ".carina-write-probe.";

    public static IReadOnlyList<StorageRootDto> Of(DriverConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return
        [
            .. (configuration.OutputRoots ?? [])
                .Where(root => root?.Name is not null && root.Path is not null)
                .Select(root => Of(root.Name!, root.Path!)),
        ];
    }

    private static StorageRootDto Of(string name, string path)
    {
        try
        {
            var room = new DriveInfo(path);

            return new StorageRootDto
            {
                Name = name,
                FreeBytes = room.AvailableFreeSpace,
                TotalBytes = room.TotalSize,
                Writable = CanWrite(path),
            };
        }
        catch (Exception error)
            when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new StorageRootDto { Name = name };
        }
    }

    private static void SweepLeftoverProbes(string path)
    {
        foreach (string leftover in Directory.EnumerateFiles(path, WriteProbePrefix + "*"))
        {
            try
            {
                File.Delete(leftover);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static bool CanWrite(string path)
    {
        try
        {
            SweepLeftoverProbes(path);

            using FileStream probe = new(
                Path.Combine(path, WriteProbePrefix + Guid.NewGuid().ToString("N")),
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.DeleteOnClose,
                    BufferSize = 0,
                }
            );

            probe.WriteByte(0);

            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
