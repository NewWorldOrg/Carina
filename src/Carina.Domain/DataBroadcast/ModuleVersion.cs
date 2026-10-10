namespace Carina.Domain.DataBroadcast;

/// <summary>
/// One version of one module of a carousel as it was put together, with when it was first and last seen
/// on the 90 kHz clock of what it was taken from.
/// </summary>
public sealed record ModuleVersion
{
    public ModuleVersion(int tag, int moduleId, int version, long firstSeen, long lastSeen, IReadOnlyList<CarouselResource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        if (resources.Count == 0)
        {
            throw new ArgumentException("A module carries at least one resource.", nameof(resources));
        }

        if (lastSeen < firstSeen)
        {
            throw new ArgumentOutOfRangeException(nameof(lastSeen), lastSeen, "A version is last seen no earlier than it is first seen.");
        }

        Tag = CarouselNumbers.Tag(tag, nameof(tag));
        ModuleId = CarouselNumbers.ModuleId(moduleId, nameof(moduleId));
        Version = CarouselNumbers.Version(version, nameof(version));
        FirstSeen = firstSeen;
        LastSeen = lastSeen;
        Resources = [.. resources];
    }

    public int Tag { get; }

    public int ModuleId { get; }

    public int Version { get; }

    public long FirstSeen { get; }

    public long LastSeen { get; private init; }

    public IReadOnlyList<CarouselResource> Resources { get; }

    public long Bytes => Resources.Sum(resource => resource.Bytes);

    public ModuleVersion SeenAt(long at) => at <= LastSeen ? this : this with { LastSeen = at };

    public bool IsTheSameAs(ModuleVersion other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return Tag == other.Tag && ModuleId == other.ModuleId && Version == other.Version;
    }
}
