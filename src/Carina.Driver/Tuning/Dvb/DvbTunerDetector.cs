namespace Carina.Driver.Tuning.Dvb;

public sealed class DvbTunerDetector : ITunerDetector
{
    private readonly Lazy<IDvbSystemCalls> calls;
    private readonly string root;
    private readonly DvbSysfs? sysfs;

    public DvbTunerDetector()
        : this(
            new Lazy<IDvbSystemCalls>(() => new LinuxDvbSystemCalls()),
            DvbDevicePaths.DeviceRoot,
            new DvbSysfs(DvbSysfs.ClassRoot)
        )
    { }

    private DvbTunerDetector(Lazy<IDvbSystemCalls> calls, string root, DvbSysfs? sysfs)
    {
        this.calls = calls;
        this.root = root;
        this.sysfs = sysfs;
    }

    /// <param name="sysfsRoot">Where the kernel files its DVB devices, or <see langword="null"/> to read nothing there.</param>
    public static DvbTunerDetector Using(IDvbSystemCalls calls, string root, string? sysfsRoot = null) =>
        new(new Lazy<IDvbSystemCalls>(() => calls), root, sysfsRoot is null ? null : new DvbSysfs(sysfsRoot));

    public IReadOnlyList<TunerDetection> Detect()
    {
        IReadOnlyList<string> frontends = DvbDeviceProbe.FrontendPathsUnder(root);

        if (frontends.Count is 0)
        {
            return [];
        }

        return [.. new DvbDeviceProbe(calls.Value).Inspect(frontends).Select(Describe)];
    }

    private TunerDetection Describe(DetectedTuner tuner)
    {
        string deviceId = DeviceIdFor(tuner.FrontendPath);
        DvbSysfsEntry filed = sysfs?.Read(tuner.FrontendPath) ?? DvbSysfsEntry.Unfiled;

        return new TunerDetection(
            deviceId,
            tuner.Receives,
            tuner.Detection,
            tuner.Problem?.Replace(tuner.FrontendPath, deviceId, StringComparison.Ordinal),
            tuner.FrontendPath,
            new DeviceHardware(
                string.IsNullOrWhiteSpace(tuner.Name) ? null : tuner.Name,
                filed.KernelDriver,
                filed.Bus,
                null
            )
        );
    }

    private static string DeviceIdFor(string frontendPath)
    {
        string adapter = Path.GetDirectoryName(frontendPath) is { } directory
            ? Path.GetFileName(directory)
            : string.Empty;

        return $"{adapter}.{Path.GetFileName(frontendPath)}";
    }
}
