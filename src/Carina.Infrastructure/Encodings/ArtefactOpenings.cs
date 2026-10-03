using System.Collections.Concurrent;

using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Infrastructure.Encodings;

public sealed class ArtefactOpenings(TimeProvider clock) : IArtefactOpenings
{
    private readonly ConcurrentDictionary<(string Root, string Name), DateTimeOffset> opened = new();

    public void Opened(OutputRoot root, string name)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrEmpty(name);

        opened[(root.Value, name)] = clock.GetUtcNow();
    }

    public DateTimeOffset? LastOpened(OutputRoot root, string name)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrEmpty(name);

        return opened.TryGetValue((root.Value, name), out DateTimeOffset at) ? at : null;
    }
}
