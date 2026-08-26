namespace Carina.Contracts;

public sealed record TunerConfigEntry
{
    public string DeviceId { get; init; } = string.Empty;

    public bool Disabled { get; init; }

    public bool LnbPower { get; init; }

    public TunerKind Kind { get; init; }

    public IReadOnlyList<string> Validate() =>
        WireName.IsUsable(DeviceId)
            ? []
            :
            [
                $"deviceId: expected one of the detected device ids, {WireName.Description}; got '{DeviceId}'.",
            ];
}

public sealed record TunerLedgerDto
{
    private readonly IReadOnlyList<TunerConfigEntry> tuners = [];

    public IReadOnlyList<TunerConfigEntry> Tuners
    {
        get => tuners;
        init => tuners = value ?? [];
    }

    public string? LoadedHash { get; init; }

    public string? SavedHash { get; init; }

    public bool HasDrifted() =>
        LoadedHash is null
        || SavedHash is null
        || !string.Equals(LoadedHash, SavedHash, StringComparison.Ordinal);
}

public sealed record TunerToggleRequest
{
    public bool? Disabled { get; init; }

    public IReadOnlyList<string> Validate() =>
        Disabled is null
            ?
            [
                "disabled: expected true to take a tuner out of service or false to put it back.",
            ]
            : [];
}
