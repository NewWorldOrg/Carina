namespace Carina.Domain.Migration;

public sealed record RescannedService
{
    public RescannedService(ServiceKey service, string name)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(name);

        Service = service;
        Name = name;
    }

    public ServiceKey Service { get; }

    public string Name { get; }

    public static IReadOnlySet<ServiceKey> InReach(IReadOnlyList<RescannedService> rescanned)
    {
        ArgumentNullException.ThrowIfNull(rescanned);

        HashSet<ServiceKey> found = [];

        foreach (RescannedService service in rescanned)
        {
            ArgumentNullException.ThrowIfNull(service, nameof(rescanned));

            found.Add(service.Service);
        }

        return found;
    }

    public static IReadOnlyDictionary<ServiceKey, string> NamedBy(IReadOnlyList<RescannedService> rescanned)
    {
        ArgumentNullException.ThrowIfNull(rescanned);

        Dictionary<ServiceKey, string> found = [];

        foreach (RescannedService service in rescanned)
        {
            ArgumentNullException.ThrowIfNull(service, nameof(rescanned));

            if (!found.TryAdd(service.Service, service.Name))
            {
                throw new ArgumentException(
                    $"A rescan names service {service.Service} once.",
                    nameof(rescanned));
            }
        }

        return found;
    }
}
