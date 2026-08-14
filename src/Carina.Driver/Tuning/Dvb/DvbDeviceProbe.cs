using Carina.Driver.Configuration;

namespace Carina.Driver.Tuning.Dvb;

public sealed record DetectedTuner(
    string FrontendPath,
    string Name,
    IReadOnlyList<DeliverySystem> DeliverySystems,
    DeviceKind Kind,
    string? Problem
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

    private DetectedTuner InspectOne(string frontendPath)
    {
        DvbFrontend? frontend = null;

        try
        {
            frontend = DvbFrontend.Open(calls, frontendPath, DvbAccess.Inspect);

            var named = frontend.TryReadHardwareName(out var name, out var nameProblem);
            var enumerated = frontend.TryReadDeliverySystems(out var systems, out var systemProblem);
            var kind = KindOf(systems, name);
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
                problems.Count is 0 ? null : string.Join("; ", problems)
            );
        }
        catch (DvbDeviceException error)
        {
            return new DetectedTuner(
                frontendPath,
                string.Empty,
                [],
                DeviceKind.Unspecified,
                error.Message
            );
        }
        finally
        {
            frontend?.Dispose();
        }
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
