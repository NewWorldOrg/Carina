namespace Carina.Contracts;

public sealed record TunerConfigEntry
{
    public string DeviceId { get; init; } = string.Empty;

    public bool Disabled { get; init; }

    public bool LnbPower { get; init; }

    public IReadOnlyList<string> Validate() =>
        WireName.IsUsable(DeviceId)
            ? []
            :
            [
                $"deviceId: expected one of the detected device ids, {WireName.Description}; got '{DeviceId}'.",
            ];
}
