using Carina.Domain.Base;

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

    public int? RemoteControlKeyId { get; private set; }

    public LogoId? LogoId { get; private set; }

    public StationLogoDeclaration LogoDeclaration { get; private set; }

    public DateTime DiscoveredAt { get; private set; }

    public DateTime LastSeenAt { get; private set; }

    public bool ReservableByDefault
        => Category is ServiceCategory.Television or ServiceCategory.Radio;

    public bool ListedInTheGuide
        => Category is not (ServiceCategory.OneSeg or ServiceCategory.Data);

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
        DateTime lastSeenAt,
        int? remoteControlKeyId = null,
        LogoId? logoId = null,
        StationLogoDeclaration logoDeclaration = StationLogoDeclaration.NotYetRead)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(serviceId);

        return new BroadcastService
        {
            NetworkId = networkId,
            ServiceId = serviceId,
            Name = ValidatedName(name),
            Category = category,
            RemoteControlKeyId = remoteControlKeyId,
            LogoId = ValidatedLogo(logoId, logoDeclaration),
            LogoDeclaration = logoDeclaration,
            DiscoveredAt = UtcTimes.Required(discoveredAt, nameof(discoveredAt)),
            LastSeenAt = UtcTimes.Required(lastSeenAt, nameof(lastSeenAt)),
        };
    }

    public void RemoteControlledBy(int? remoteControlKeyId)
    {
        if (remoteControlKeyId is null)
        {
            return;
        }

        RemoteControlKeyId = remoteControlKeyId;
    }

    public bool NamesTheLogo(LogoId logoId)
    {
        ArgumentNullException.ThrowIfNull(logoId);

        if (LogoDeclaration is StationLogoDeclaration.InTheCommonDataTable && logoId.Equals(LogoId))
        {
            return false;
        }

        LogoId = logoId;
        LogoDeclaration = StationLogoDeclaration.InTheCommonDataTable;

        return true;
    }

    public bool BroadcastsNoLogo()
    {
        if (LogoDeclaration is StationLogoDeclaration.NoPictureIsBroadcast)
        {
            return false;
        }

        LogoId = null;
        LogoDeclaration = StationLogoDeclaration.NoPictureIsBroadcast;

        return true;
    }

    public void Describe(string name, ServiceCategory category, DateTime at)
    {
        Name = ValidatedName(name);
        Category = category;
        LastSeenAt = UtcTimes.Required(at, nameof(at));
    }

    private static LogoId? ValidatedLogo(LogoId? logoId, StationLogoDeclaration declaration)
    {
        if ((logoId is not null) != (declaration is StationLogoDeclaration.InTheCommonDataTable))
        {
            throw new ArgumentException(
                "A service names a logo exactly when its declaration says the logo is in the common data table,"
                + $" but this one says {declaration} beside {(logoId is null ? "no logo" : logoId.Value)}.",
                nameof(logoId));
        }

        return logoId;
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
