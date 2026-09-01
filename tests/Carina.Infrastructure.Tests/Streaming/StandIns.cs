using System.Runtime.Versioning;

namespace Carina.Infrastructure.Tests.Streaming;

[SupportedOSPlatform("linux")]
public sealed class StandIns : IDisposable
{
    private readonly string room = Directory.CreateTempSubdirectory("carina-transcode").FullName;

    public string Room => room;

    public void Dispose() => Directory.Delete(room, recursive: true);

    public string Named(string name) => Path.Combine(room, name);

    public string Script(string body)
    {
        string path = Named($"stand-in-{Guid.NewGuid():N}");

        File.WriteAllText(path, $"#!/bin/sh\n{body}\n");
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        return path;
    }

    public string Node()
    {
        string path = Named($"node-{Guid.NewGuid():N}");

        File.WriteAllText(path, string.Empty);

        return path;
    }

    public async Task<bool> NothingIsLeftOf(IEnumerable<int> pids)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (pids.All(Gone))
            {
                return true;
            }

            await Task.Delay(50);
        }

        return false;
    }

    private static bool Gone(int pid)
    {
        string named = $"/proc/{pid}/cmdline";

        if (!File.Exists(named))
        {
            return true;
        }

        try
        {
            return File.ReadAllBytes(named).Length is 0;
        }
        catch (IOException)
        {
            return true;
        }
    }
}
