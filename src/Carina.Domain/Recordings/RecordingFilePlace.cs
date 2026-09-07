namespace Carina.Domain.Recordings;

public static class RecordingFilePlace
{
    public static bool LiesDirectlyUnder(string room, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string held = Path.TrimEndingDirectorySeparator(Path.GetFullPath(room));
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        return string.Equals(Path.GetDirectoryName(full), held, StringComparison.Ordinal);
    }
}
