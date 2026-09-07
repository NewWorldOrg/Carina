using System.Globalization;
using System.Runtime.Versioning;

using Carina.TestSupport;

namespace Carina.Infrastructure.Tests;

[SupportedOSPlatform("linux")]
public sealed class StandIns : IDisposable
{
    private readonly string room = Directory.CreateTempSubdirectory("carina-transcode").FullName;

    public string Room => room;

    public void Dispose() => Directory.Delete(room, recursive: true);

    public string Named(string name) => Path.Combine(room, name);

    public string Script(string body)
        => StandInProgramme.Written(Named($"stand-in-{Guid.NewGuid():N}"), body);

    public string Node()
    {
        string path = Named($"node-{Guid.NewGuid():N}");

        File.WriteAllText(path, string.Empty);

        return path;
    }

    public static IEnumerable<int> Pids(string written)
        => File.ReadAllLines(written)
            .Where(line => line.Length > 0)
            .Select(line => int.Parse(line, CultureInfo.InvariantCulture));

    public static Task WroteDown(string pids, int howMany)
        => Eventually.Happens(
            () => Written(pids).Count >= howMany,
            $"the stand-in wrote down {howMany} process identifiers");

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

    private static IReadOnlyList<int> Written(string pids)
    {
        try
        {
            return [.. Pids(pids)];
        }
        catch (IOException)
        {
            return [];
        }
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
