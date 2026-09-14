using Carina.Domain.Recordings;

namespace Carina.Domain.Integrity;

public sealed record DeclaredFile
{
    public DeclaredFile(OutputRoot root, string path)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (path.StartsWith('/'))
        {
            throw new ArgumentException(
                "A path is read from the output root down, so it does not start at the top of the disk.",
                nameof(path));
        }

        Root = root;
        Path = path;
    }

    public OutputRoot Root { get; }

    public string Path { get; }
}
