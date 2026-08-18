using Carina.Driver.Configuration;

namespace Carina.Driver.Tuning;

public sealed record TunerContradiction(
    string DeviceId,
    DeviceKind Declared,
    IReadOnlyList<DeviceKind> Receives
)
{
    public string Detail =>
        $"The ledger calls '{DeviceId}' a {Name(Declared)} tuner and the tuner reports that it receives {Names(Receives)}. Until the ledger and the hardware agree the driver will not hand it out, because tuning it as a {Name(Declared)} tuner would silently receive nothing.";

    private static string Names(IReadOnlyList<DeviceKind> kinds) =>
        kinds.Count is 0 ? "nothing" : string.Join(" and ", kinds.Select(Name));

    private static string Name(DeviceKind kind) => kind.ToString().ToLowerInvariant();
}

public static class TunerLedgerCheck
{
    public static IReadOnlyList<TunerContradiction> Contradictions(
        IReadOnlyList<DeviceSettings>? declared,
        IReadOnlyList<TunerDetection> detected
    )
    {
        var contradictions = new List<TunerContradiction>();

        foreach (DeviceSettings device in declared ?? [])
        {
            if (device?.Id is not { } deviceId || device.Kind is DeviceKind.Unspecified)
            {
                continue;
            }

            TunerDetection? detection = detected.FirstOrDefault(candidate =>
                string.Equals(candidate.DeviceId, deviceId, StringComparison.Ordinal)
            );

            if (detection is null || detection.Receives.Count is 0)
            {
                continue;
            }

            if (!detection.Receives.Contains(device.Kind))
            {
                contradictions.Add(
                    new TunerContradiction(deviceId, device.Kind, detection.Receives)
                );
            }
        }

        return contradictions;
    }
}
