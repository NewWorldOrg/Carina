using System.Security.Cryptography;

namespace Carina.Infrastructure.Tests.Integrity;

internal sealed class TempTree : IDisposable
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-integrity-");

    public string Root => directory.FullName;

    public string Under(params string[] segments) => Path.Combine([Root, .. segments]);

    public TempTree Holding(string name, byte[] bytes)
    {
        string full = Path.Combine(Root, name.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);

        return this;
    }

    public TempTree Holding(string name, int sizeBytes)
        => Holding(name, Filled(name, sizeBytes));

    public TempTree HoldingDirectory(string name)
    {
        Directory.CreateDirectory(Path.Combine(Root, name.Replace('/', Path.DirectorySeparatorChar)));

        return this;
    }

    public IReadOnlyList<string> Snapshot()
        => [.. Directory
            .EnumerateFileSystemEntries(Root, "*", SearchOption.AllDirectories)
            .Select(Describe)
            .Order(StringComparer.Ordinal)];

    public void Dispose() => directory.Delete(recursive: true);

    private string Describe(string entry)
    {
        string relative = Path.GetRelativePath(Root, entry).Replace('\\', '/');

        if (Directory.Exists(entry))
        {
            return $"dir {relative}";
        }

        byte[] bytes = File.ReadAllBytes(entry);

        return $"file {relative} {bytes.Length} {Convert.ToHexString(SHA256.HashData(bytes))}";
    }

    private static byte[] Filled(string name, int sizeBytes)
    {
        byte[] bytes = new byte[sizeBytes];

        for (int index = 0; index < sizeBytes; index++)
        {
            bytes[index] = (byte)(name[index % name.Length] + index);
        }

        return bytes;
    }
}
