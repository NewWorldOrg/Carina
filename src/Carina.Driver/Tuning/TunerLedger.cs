using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

using Carina.Contracts;
using Carina.Driver.Configuration;

namespace Carina.Driver.Tuning;

public enum LedgerRefusal
{
    None,
    Empty,
    Malformed,
    UnknownDevice,
    UndeterminedKind,
    Unwritable,
}

public sealed record LedgerRevision
{
    private LedgerRevision(
        IReadOnlyList<DeviceSettings>? devices,
        LedgerRefusal refusal,
        string detail
    )
    {
        Devices = devices;
        Refusal = refusal;
        Detail = detail;
    }

    public IReadOnlyList<DeviceSettings>? Devices { get; }

    public LedgerRefusal Refusal { get; }

    public string Detail { get; }

    public static LedgerRevision Accepted(IReadOnlyList<DeviceSettings> devices) =>
        new(devices, LedgerRefusal.None, string.Empty);

    public static LedgerRevision Refused(LedgerRefusal refusal, string detail) =>
        new(null, refusal, detail);

    public bool TryGetDevices([NotNullWhen(true)] out IReadOnlyList<DeviceSettings>? devices)
    {
        devices = Devices;

        return devices is not null;
    }
}

public static class TunerLedger
{
    public static IReadOnlyList<TunerConfigEntry> Entries(
        IReadOnlyList<DeviceSettings>? devices
    ) =>
        [
            .. (devices ?? [])
                .Where(device => device?.Id is not null)
                .Select(device => new TunerConfigEntry
                {
                    DeviceId = device.Id!,
                    Disabled = !device.Enabled,
                    LnbPower = device.LnbPower,
                }),
        ];

    public static string Fingerprint(IReadOnlyList<DeviceSettings>? devices)
    {
        string canonical = string.Join(
            "\n",
            (devices ?? [])
                .Where(device => device?.Id is not null)
                .OrderBy(device => device.Id, StringComparer.Ordinal)
                .Select(Render)
        );

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static LedgerRevision Revise(
        IReadOnlyList<TunerConfigEntry>? requested,
        IReadOnlyList<TunerDetection> detected,
        IReadOnlyList<DeviceSettings>? current
    )
    {
        IReadOnlyList<TunerConfigEntry> wanted = requested ?? [];

        if (wanted.Count is 0)
        {
            return LedgerRevision.Refused(
                LedgerRefusal.Empty,
                "A ledger with no tuners in it leaves the driver with nothing to record on, and saving one is how a configuration gets wiped by accident. Turn the tuners off one at a time instead, or ask for the ledger to be cleared and detected again."
            );
        }

        var devices = new List<DeviceSettings>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (TunerConfigEntry? entry in wanted)
        {
            if (entry is null)
            {
                return LedgerRevision.Refused(
                    LedgerRefusal.Malformed,
                    "One of the entries is nothing at all."
                );
            }

            if (entry.Validate() is { Count: > 0 } problems)
            {
                return LedgerRevision.Refused(
                    LedgerRefusal.Malformed,
                    string.Join(" ", problems)
                );
            }

            if (!seen.Add(entry.DeviceId))
            {
                return LedgerRevision.Refused(
                    LedgerRefusal.Malformed,
                    $"'{entry.DeviceId}' is named by more than one entry, and one tuner cannot be turned both on and off."
                );
            }

            TunerDetection? detection = detected.FirstOrDefault(candidate =>
                string.Equals(candidate.DeviceId, entry.DeviceId, StringComparison.Ordinal)
            );

            if (detection is null)
            {
                return LedgerRevision.Refused(
                    LedgerRefusal.UnknownDevice,
                    $"No tuner called '{entry.DeviceId}' was detected; this driver found {Names(detected)}."
                );
            }

            DeviceSettings? settings = current?.FirstOrDefault(device =>
                string.Equals(device?.Id, entry.DeviceId, StringComparison.Ordinal)
            );

            if (!TryResolveKind(detection, settings, out DeviceKind kind))
            {
                return LedgerRevision.Refused(
                    LedgerRefusal.UndeterminedKind,
                    $"The tuner '{entry.DeviceId}' did not say what it receives and the ledger has never held it, so saving it would be guessing at what it can tune."
                );
            }

            devices.Add(
                new DeviceSettings(
                    entry.DeviceId,
                    kind,
                    detection.DevicePath ?? settings?.DevicePath,
                    !entry.Disabled,
                    entry.LnbPower
                )
            );
        }

        return LedgerRevision.Accepted(devices);
    }

    private static bool TryResolveKind(
        TunerDetection detection,
        DeviceSettings? settings,
        out DeviceKind kind
    )
    {
        DeviceKind declared = settings?.Kind ?? DeviceKind.Unspecified;

        if (declared is not DeviceKind.Unspecified && detection.Receives.Contains(declared))
        {
            kind = declared;

            return true;
        }

        if (detection.Receives.Count > 0)
        {
            kind = detection.Receives[0];

            return true;
        }

        kind = declared;

        return declared is not DeviceKind.Unspecified;
    }

    private static string Render(DeviceSettings device) =>
        string.Join(
            "\t",
            device.Id,
            device.Kind,
            device.DevicePath ?? string.Empty,
            device.Enabled ? "enabled" : "disabled",
            device.LnbPower ? "lnbPower" : "noLnbPower"
        );

    private static string Names(IReadOnlyList<TunerDetection> detected) =>
        detected.Count is 0
            ? "none at all"
            : string.Join(", ", detected.Select(detection => $"'{detection.DeviceId}'"));
}
