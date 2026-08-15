using Carina.Contracts;

namespace Carina.Api.Requests;

public sealed record TunerEntryRequest
{
    public string? DeviceId { get; init; }

    public bool Disabled { get; init; }

    public bool LnbPower { get; init; }

    public TunerConfigEntry ToEntry() => new()
    {
        DeviceId = DeviceId ?? string.Empty,
        Disabled = Disabled,
        LnbPower = LnbPower,
    };
}

public sealed record TunerLedgerRequest
{
    public IReadOnlyList<TunerEntryRequest>? Tuners { get; init; }

    public IReadOnlyList<TunerConfigEntry> ToEntries()
        => [.. (Tuners ?? []).Select(entry => entry.ToEntry())];
}

public sealed record ToggleTunerRequest
{
    public bool? Disabled { get; init; }
}
