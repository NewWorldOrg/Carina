namespace Carina.Domain.DataBroadcast;

/// <summary>
/// One version of one module of a carousel as it was put together, with when it was first and last seen on
/// the <see cref="StreamClock"/>, never a raw PTS.
/// </summary>
public sealed record ModuleVersion
{
    public const int HeaderBytes = sizeof(ushort) + sizeof(byte) + sizeof(ulong) + sizeof(ulong) + sizeof(uint);

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

    public TimeSpan FirstSeenAt => StreamClock.ToTime(FirstSeen);

    public TimeSpan LastSeenAt => StreamClock.ToTime(LastSeen);

    public IReadOnlyList<CarouselResource> Resources { get; }

    /// <summary>
    /// The bytes it takes in a record: its module id, version, sightings and length, and each resource.
    /// </summary>
    public long Bytes => HeaderBytes + Resources.Sum(resource => resource.Bytes);

    public ModuleVersion SeenAt(long at) => at <= LastSeen ? this : this with { LastSeen = at };

    public ModuleVersion Shifted(long by) => new(Tag, ModuleId, Version, FirstSeen + by, LastSeen + by, Resources);

    public bool CarriesTheSameAs(ModuleVersion other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return Resources.Count == other.Resources.Count
               && Resources.Zip(other.Resources).All(pair => pair.First.IsTheSameAs(pair.Second));
    }
}
