using Carina.Domain.Integrity;
using Carina.Domain.Recordings;

namespace Carina.Infrastructure.Configuration;

/// <summary>
/// Reads a setting of the form <c>name=/path;name=/path</c> into the roots and the paths they are
/// mounted at. The same shape names the roots this process reads recordings from and the roots it
/// writes artefacts into, so it is read in one place.
/// </summary>
internal static class MountedRoots
{
    private const char BetweenRoots = ';';

    private const char BetweenNameAndPath = '=';

    public static IReadOnlyList<StorageRootPath> Read(string section, string name, string? setting)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return [];
        }

        List<StorageRootPath> mounted = [];
        HashSet<string> named = new(StringComparer.Ordinal);

        foreach (string entry in setting.Split(BetweenRoots, StringSplitOptions.TrimEntries))
        {
            if (entry.Length is 0)
            {
                continue;
            }

            int split = entry.IndexOf(BetweenNameAndPath, StringComparison.Ordinal);

            if (split < 0)
            {
                throw new ArgumentException(
                    $"{section}:{name} reads a ';'-separated list of name=/path, and '{entry}' names no path.",
                    name);
            }

            StorageRootPath read = Mounted(section, name, entry[..split].Trim(), entry[(split + 1)..].Trim());

            if (!named.Add(read.Root.Value))
            {
                throw new ArgumentException(
                    $"{section}:{name} mounts '{read.Root.Value}' twice, so which path it means is unanswerable.",
                    name);
            }

            mounted.Add(read);
        }

        return mounted;
    }

    private static StorageRootPath Mounted(string section, string name, string root, string path)
    {
        try
        {
            return new StorageRootPath(new OutputRoot(root), path);
        }
        catch (ArgumentException refusal)
        {
            throw new ArgumentException(
                $"{section}:{name} does not describe a mounted output root: {refusal.Message}",
                name,
                refusal);
        }
    }
}
