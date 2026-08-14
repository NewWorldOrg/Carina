namespace Carina.Domain.Scans;

public sealed record ScanDifference
{
    public static readonly ScanDifference Nothing = new([], []);

    public ScanDifference(
        IReadOnlyList<ScanServiceChange> services,
        IReadOnlyList<RotationDeparture> departures)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(departures);

        Services = services;
        Departures = departures;
    }

    public IReadOnlyList<ScanServiceChange> Services { get; }

    public IReadOnlyList<RotationDeparture> Departures { get; }

    public IReadOnlyList<ScanServiceChange> Added => Of(ScanChangeKind.Added);

    public IReadOnlyList<ScanServiceChange> Updated => Of(ScanChangeKind.Updated);

    public IReadOnlyList<ScanServiceChange> Missing => Of(ScanChangeKind.Missing);

    public bool ChangesNothing => Services.Count == 0;

    private IReadOnlyList<ScanServiceChange> Of(ScanChangeKind kind)
        => [.. Services.Where(change => change.Kind == kind)];
}
