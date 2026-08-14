namespace Carina.Domain.Channels;

public sealed class BroadcastService
{
    public const int NameMaxLength = 256;

    private BroadcastService()
    {
    }

    public NetworkId NetworkId { get; private set; } = null!;

    public ServiceId ServiceId { get; private set; } = null!;

    public string Name { get; private set; } = string.Empty;

    public ServiceCategory Category { get; private set; }

    public DateTime DiscoveredAt { get; private set; }

    public DateTime LastSeenAt { get; private set; }

    public bool ReservableByDefault
        => Category is ServiceCategory.Television or ServiceCategory.Radio;

    public static BroadcastService Discover(
        NetworkId networkId,
        ServiceId serviceId,
        string name,
        ServiceCategory category,
        DateTime at)
        => Rehydrate(networkId, serviceId, name, category, at, at);

    public static BroadcastService Rehydrate(
        NetworkId networkId,
        ServiceId serviceId,
        string name,
        ServiceCategory category,
        DateTime discoveredAt,
        DateTime lastSeenAt)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(serviceId);

        return new BroadcastService
        {
            NetworkId = networkId,
            ServiceId = serviceId,
            Name = ValidatedName(name),
            Category = category,
            DiscoveredAt = UtcTimes.Required(discoveredAt, nameof(discoveredAt)),
            LastSeenAt = UtcTimes.Required(lastSeenAt, nameof(lastSeenAt)),
        };
    }

    public void Describe(string name, ServiceCategory category, DateTime at)
    {
        Name = ValidatedName(name);
        Category = category;
        LastSeenAt = UtcTimes.Required(at, nameof(at));
    }

    private static string ValidatedName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (name.Length > NameMaxLength)
        {
            throw new ArgumentException(
                $"A service name is at most {NameMaxLength} characters, but this one has {name.Length}.",
                nameof(name));
        }

        return name;
    }
}
