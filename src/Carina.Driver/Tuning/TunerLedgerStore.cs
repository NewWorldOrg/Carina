using Carina.Contracts;
using Carina.Driver.Configuration;

namespace Carina.Driver.Tuning;

public sealed class TunerLedgerStore(DriverConfiguration configuration, string? path)
{
    private readonly Lock gate = new();

    public string LoadedHash { get; } = TunerLedger.Fingerprint(configuration.Devices);

    public TunerLedgerDto View()
    {
        DriverConfiguration? saved = Saved();

        return new TunerLedgerDto
        {
            Tuners = TunerLedger.Entries(saved?.Devices ?? configuration.Devices),
            LoadedHash = LoadedHash,
            SavedHash = saved is null ? null : TunerLedger.Fingerprint(saved.Devices),
        };
    }

    /// <summary>
    /// Saves the tuners asked for. When <paramref name="expectedSavedHash"/> is given, the save is taken
    /// only while the saved ledger is still the one that hash was read from.
    /// </summary>
    public LedgerRevision Save(
        IReadOnlyList<TunerConfigEntry>? requested,
        IReadOnlyList<TunerDetection> detected,
        string? expectedSavedHash = null
    )
    {
        lock (gate)
        {
            DriverConfiguration? saved = Saved();

            if (expectedSavedHash is not null
                && !string.Equals(expectedSavedHash, SavedHashOf(saved), StringComparison.Ordinal))
            {
                return LedgerRevision.Refused(
                    LedgerRefusal.Stale,
                    "The ledger has been saved since this one was read; read it again and save from what it says now."
                );
            }

            LedgerRevision revision = TunerLedger.Revise(
                requested,
                detected,
                saved?.Devices ?? configuration.Devices
            );

            return revision.TryGetDevices(out IReadOnlyList<DeviceSettings>? devices)
                ? Write(saved, devices, revision)
                : revision;
        }
    }

    /// <summary>
    /// Turns the low-noise block power of one satellite tuner on or off in the saved ledger, leaving
    /// every other setting as it is saved. The running driver takes it on its next start.
    /// </summary>
    public LedgerRevision SwitchLnbPower(string deviceId, bool on)
    {
        lock (gate)
        {
            DriverConfiguration? saved = Saved();
            IReadOnlyList<DeviceSettings> current = saved?.Devices ?? configuration.Devices ?? [];

            if (current.FirstOrDefault(device => string.Equals(device?.Id, deviceId, StringComparison.Ordinal))
                is not { } named)
            {
                return LedgerRevision.Refused(
                    LedgerRefusal.NotInLedger,
                    $"The ledger holds no tuner called '{deviceId}'."
                );
            }

            if (on && named.Kind is not DeviceKind.Satellite)
            {
                return LedgerRevision.Refused(
                    LedgerRefusal.Malformed,
                    $"'{deviceId}' is not a satellite tuner, and only a satellite tuner powers a low-noise block."
                );
            }

            IReadOnlyList<DeviceSettings> devices =
            [
                .. current.Select(device => ReferenceEquals(device, named) ? device with { LnbPower = on } : device),
            ];

            return Write(saved, devices, LedgerRevision.Accepted(devices));
        }
    }

    private static string? SavedHashOf(DriverConfiguration? saved)
        => saved is null ? null : TunerLedger.Fingerprint(saved.Devices);

    private LedgerRevision Write(
        DriverConfiguration? saved,
        IReadOnlyList<DeviceSettings> devices,
        LedgerRevision revision
    )
    {
        IReadOnlyList<string> problems = DriverConfigurationReader.DeviceProblems(
            devices,
            configuration.Tuner?.Backend ?? TunerBackend.Unspecified
        );

        if (problems.Count > 0)
        {
            return LedgerRevision.Refused(
                LedgerRefusal.Malformed,
                WithoutDeviceNodes(
                    $"This ledger would leave the driver unable to start: {string.Join(" ", problems)}",
                    devices
                )
            );
        }

        if (path is null)
        {
            return LedgerRevision.Refused(
                LedgerRefusal.Unwritable,
                "This driver was never told where its ledger is kept, so it has nowhere to save one."
            );
        }

        string json = DriverConfigurationWriter.Serialize(
            (saved ?? configuration) with
            {
                Devices = devices,
            }
        );

        try
        {
            AtomicFile.Replace(path, json);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return LedgerRevision.Refused(
                LedgerRefusal.Unwritable,
                "The ledger could not be written where this driver keeps it; the one already saved is untouched."
            );
        }

        return revision;
    }

    private static string WithoutDeviceNodes(
        string detail,
        IReadOnlyList<DeviceSettings> devices
    )
    {
        foreach (DeviceSettings device in devices)
        {
            if (device.DevicePath is { Length: > 0 } node && device.Id is { } deviceId)
            {
                detail = detail.Replace(node, deviceId, StringComparison.Ordinal);
            }
        }

        return detail;
    }

    private DriverConfiguration? Saved()
    {
        if (path is null)
        {
            return null;
        }

        try
        {
            return DriverConfigurationReader.Parse(File.ReadAllText(path));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
