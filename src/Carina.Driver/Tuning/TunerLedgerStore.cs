using Carina.Contracts;
using Carina.Driver.Configuration;

namespace Carina.Driver.Tuning;

public sealed class TunerLedgerStore(DriverConfiguration configuration, string? path)
{
    public string LoadedHash { get; } = TunerLedger.Fingerprint(configuration.Devices);

    public TunerLedgerDto View()
    {
        var saved = Saved();

        return new TunerLedgerDto
        {
            Tuners = TunerLedger.Entries(saved?.Devices ?? configuration.Devices),
            LoadedHash = LoadedHash,
            SavedHash = saved is null ? null : TunerLedger.Fingerprint(saved.Devices),
        };
    }

    public LedgerRevision Save(
        IReadOnlyList<TunerConfigEntry>? requested,
        IReadOnlyList<TunerDetection> detected
    )
    {
        var saved = Saved();

        var revision = TunerLedger.Revise(
            requested,
            detected,
            saved?.Devices ?? configuration.Devices
        );

        if (!revision.TryGetDevices(out var devices))
        {
            return revision;
        }

        var problems = DriverConfigurationReader.DeviceProblems(
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

        var json = DriverConfigurationWriter.Serialize(
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
        foreach (var device in devices)
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
