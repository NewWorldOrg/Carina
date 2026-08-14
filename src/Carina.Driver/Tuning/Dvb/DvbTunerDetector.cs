namespace Carina.Driver.Tuning.Dvb;

public sealed class DvbTunerDetector : ITunerDetector
{
    private readonly Lazy<IDvbSystemCalls> calls;
    private readonly string root;

    public DvbTunerDetector()
        : this(
            new Lazy<IDvbSystemCalls>(() => new LinuxDvbSystemCalls()),
            DvbDevicePaths.DeviceRoot
        )
    { }

    private DvbTunerDetector(Lazy<IDvbSystemCalls> calls, string root)
    {
        this.calls = calls;
        this.root = root;
    }

    public static DvbTunerDetector Using(IDvbSystemCalls calls, string root) =>
        new(new Lazy<IDvbSystemCalls>(() => calls), root);

    public IReadOnlyList<TunerDetection> Detect()
    {
        var frontends = DvbDeviceProbe.FrontendPathsUnder(root);

        if (frontends.Count is 0)
        {
            return [];
        }

        return [.. new DvbDeviceProbe(calls.Value).Inspect(frontends).Select(Describe)];
    }

    private static TunerDetection Describe(DetectedTuner tuner)
    {
        var deviceId = DeviceIdFor(tuner.FrontendPath);

        return new TunerDetection(
            deviceId,
            tuner.Receives,
            tuner.Detection,
            tuner.Problem?.Replace(tuner.FrontendPath, deviceId, StringComparison.Ordinal)
        );
    }

    private static string DeviceIdFor(string frontendPath)
    {
        var adapter = Path.GetDirectoryName(frontendPath) is { } directory
            ? Path.GetFileName(directory)
            : string.Empty;

        return $"{adapter}.{Path.GetFileName(frontendPath)}";
    }
}
