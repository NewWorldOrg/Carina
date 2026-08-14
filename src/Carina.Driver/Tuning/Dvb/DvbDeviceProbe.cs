using Carina.Contracts;
using Carina.Driver.Configuration;

namespace Carina.Driver.Tuning.Dvb;

public sealed record DetectedTuner(
    string FrontendPath,
    string Name,
    IReadOnlyList<DeliverySystem> DeliverySystems,
    DeviceKind Kind,
    string? Problem,
    DeviceDetection Detection,
    IReadOnlyList<DeviceKind> Receives
);

public sealed class DvbDeviceProbe(IDvbSystemCalls calls)
{
    private const string AdapterGlob = "adapter*";
    private const string FrontendGlob = "frontend*";

    public static IReadOnlyList<string> FrontendPathsUnder(string root)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        return
        [
            .. Directory
                .EnumerateDirectories(root, AdapterGlob)
                .SelectMany(adapter => Directory.EnumerateFiles(adapter, FrontendGlob))
                .Order(StringComparer.Ordinal),
        ];
    }

    public IReadOnlyList<DetectedTuner> Inspect(IEnumerable<string> frontendPaths) =>
        [.. frontendPaths.Select(InspectOne)];

    public static DeviceKind KindOf(IReadOnlyList<DeliverySystem> systems, string name)
    {
        var terrestrial = systems.Contains(DeliverySystem.IsdbTerrestrial);
        var satellite = systems.Contains(DeliverySystem.IsdbSatellite);

        if (terrestrial && !satellite)
        {
            return DeviceKind.Terrestrial;
        }

        if (satellite && !terrestrial)
        {
            return DeviceKind.Satellite;
        }

        return KindFromName(name);
    }

    public static IReadOnlyList<DeviceKind> KindsOf(
        IReadOnlyList<DeliverySystem> systems,
        string name
    )
    {
        var enumerated = new List<DeviceKind>();

        foreach (var system in systems)
        {
            var kind = KindFromDeliverySystem(system);

            if (kind is not DeviceKind.Unspecified && !enumerated.Contains(kind))
            {
                enumerated.Add(kind);
            }
        }

        if (enumerated.Count > 0)
        {
            return enumerated;
        }

        return KindFromName(name) switch
        {
            DeviceKind.Unspecified => [],
            var kind => [kind],
        };
    }

    private DetectedTuner InspectOne(string frontendPath)
    {
        DvbFrontend? frontend = null;

        try
        {
            frontend = DvbFrontend.Open(calls, frontendPath, DvbAccess.Inspect);

            var named = frontend.TryReadHardwareName(out var name, out var nameProblem);
            var enumerated = frontend.TryReadDeliverySystems(out var systems, out var systemProblem);
            var kind = KindOf(systems, name);
            var receives = KindsOf(systems, name);
            var problems = new List<string>();

            if (!enumerated)
            {
                problems.Add(systemProblem);
            }

            if (!named)
            {
                problems.Add(nameProblem);
            }

            if (kind is DeviceKind.Unspecified)
            {
                problems.Add(
                    "nothing it reported says whether it receives terrestrial or satellite"
                );
            }

            return new DetectedTuner(
                frontendPath,
                name,
                systems,
                kind,
                problems.Count is 0 ? null : string.Join("; ", problems),
                receives.Count is 0 ? DeviceDetection.Unreadable : DeviceDetection.Detected,
                receives
            );
        }
        catch (DvbDeviceException error)
        {
            return new DetectedTuner(
                frontendPath,
                string.Empty,
                [],
                DeviceKind.Unspecified,
                error.Message,
                DetectionFrom(error.Error),
                []
            );
        }
        finally
        {
            frontend?.Dispose();
        }
    }

    private static DeviceDetection DetectionFrom(int error) =>
        error switch
        {
            Errno.Busy => DeviceDetection.Busy,
            Errno.PermissionDenied or Errno.NotPermitted => DeviceDetection.PermissionDenied,
            _ => DeviceDetection.Unreadable,
        };

    private static DeviceKind KindFromDeliverySystem(DeliverySystem system)
    {
        if (system == DeliverySystem.IsdbTerrestrial)
        {
            return DeviceKind.Terrestrial;
        }

        return system == DeliverySystem.IsdbSatellite
            ? DeviceKind.Satellite
            : DeviceKind.Unspecified;
    }

    private static DeviceKind KindFromName(string name)
    {
        if (Mentions(name, "ISDB-T") || Mentions(name, "ISDBT"))
        {
            return DeviceKind.Terrestrial;
        }

        if (Mentions(name, "ISDB-S") || Mentions(name, "ISDBS"))
        {
            return DeviceKind.Satellite;
        }

        return DeviceKind.Unspecified;
    }

    private static bool Mentions(string name, string what) =>
        name.Contains(what, StringComparison.OrdinalIgnoreCase);
}
