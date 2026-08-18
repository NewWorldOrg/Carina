using System.Text;

namespace Carina.Driver.Configuration;

public static class AtomicFile
{
    public static void Replace(string path, string contents)
    {
        string staged = Stage(path, contents);

        try
        {
            Commit(staged, path);
        }
        catch
        {
            Discard(staged);

            throw;
        }
    }

    public static string Stage(string path, string contents)
    {
        string target = Path.GetFullPath(path);

        string directory =
            Path.GetDirectoryName(target)
            ?? throw new IOException($"'{path}' has no directory to be written beside.");

        string staged = Path.Combine(
            directory,
            $".{Path.GetFileName(target)}.{Guid.NewGuid():N}"
        );

        try
        {
            using var stream = new FileStream(
                staged,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None
            );

            stream.Write(Encoding.UTF8.GetBytes(contents));
            stream.Flush(flushToDisk: true);
        }
        catch
        {
            Discard(staged);

            throw;
        }

        return staged;
    }

    public static void Commit(string staged, string path) =>
        File.Move(staged, Path.GetFullPath(path), overwrite: true);

    private static void Discard(string staged)
    {
        try
        {
            File.Delete(staged);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
