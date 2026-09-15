using Microsoft.Extensions.Logging;

namespace Carina.Driver.Recording;

public static class OutputRoom
{
    public static FileErasure? Missing(string room, string outputRoot, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            using IEnumerator<string> walking = Directory
                .EnumerateFiles(room, "*", SearchOption.AllDirectories)
                .GetEnumerator();

            if (!walking.MoveNext())
            {
                return FileErasure.Refused(
                    ErasureRefusal.RootOutOfReach,
                    $"Output root '{outputRoot}' holds no file at all, which is what it looks like when its "
                        + "mount has gone, so nothing under it is removed."
                );
            }

            return null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                failure,
                "Output root {Root} could not be read, so nothing under it is removed.",
                outputRoot
            );

            return FileErasure.Refused(
                ErasureRefusal.RootOutOfReach,
                $"Output root '{outputRoot}' could not be read, so a file reported missing under it says "
                    + "nothing about whether it was ever there, and nothing under it is removed."
            );
        }
    }
}
