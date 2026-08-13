namespace Carina.TestSupport;

public sealed class TempSocket : IDisposable
{
    private readonly DirectoryInfo directory;

    public TempSocket()
    {
        directory = Directory.CreateTempSubdirectory("carina-socket-");
        Path = System.IO.Path.Combine(directory.FullName, "driver.sock");
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            directory.Delete(recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
