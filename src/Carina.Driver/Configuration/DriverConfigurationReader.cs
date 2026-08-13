using System.Text.Json;

namespace Carina.Driver.Configuration;

public sealed record DriverConfigurationResult
{
    private DriverConfigurationResult(
        DriverConfiguration? configuration,
        IReadOnlyList<string> problems
    )
    {
        Configuration = configuration;
        Problems = problems;
    }

    public DriverConfiguration? Configuration { get; }

    public IReadOnlyList<string> Problems { get; }

    public static DriverConfigurationResult Usable(DriverConfiguration configuration) =>
        new(configuration, []);

    public static DriverConfigurationResult Unusable(IReadOnlyList<string> problems) =>
        new(null, problems.Count is 0 ? ["file: the configuration is not usable."] : problems);

    public bool TryGetConfiguration(
        out DriverConfiguration configuration,
        out IReadOnlyList<string> problems
    )
    {
        configuration = Configuration!;
        problems = Problems;

        return Configuration is not null;
    }
}

public static class DriverConfigurationReader
{
    private const string SocketRoot = "/run/";
    private const string DeviceRoot = "/dev/";

    private const int MinShutdownGraceHours = 1;
    private const int MaxShutdownGraceHours = 168;
    private const int MinLiveSessionMinutes = 1;
    private const int MaxLiveSessionMinutes = 1440;
    private const int MaxNameLength = 64;

    public static DriverConfigurationResult ReadFile(
        string? path,
        bool checkTheFilesystem = true
    )
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Failed("file: no configuration path was given.");
        }

        if (Directory.Exists(path))
        {
            return Failed($"file: '{path}' is a directory, not a configuration file.");
        }

        try
        {
            var result = Read(File.ReadAllText(path));

            if (!checkTheFilesystem)
            {
                return result;
            }

            return result.TryGetConfiguration(out var configuration, out _)
                ? CheckTheFilesystem(configuration)
                : result;
        }
        catch (FileNotFoundException)
        {
            return Failed($"file: no configuration at '{path}'.");
        }
        catch (DirectoryNotFoundException)
        {
            return Failed($"file: no directory on the way to '{path}'.");
        }
        catch (UnauthorizedAccessException)
        {
            return Failed(
                $"file: '{path}' exists but this process may not read it. Check the owner and the mode of the file and of every directory above it."
            );
        }
        catch (IOException error)
        {
            return Failed($"file: '{path}' could not be read: {error.Message}");
        }
    }

    public static DriverConfigurationResult CheckTheFilesystem(
        DriverConfiguration configuration
    )
    {
        var problems = new List<string>();
        var roots = configuration.OutputRoots ?? [];

        for (var index = 0; index < roots.Count; index++)
        {
            var path = roots[index]!.Path!;

            if (!Directory.Exists(path))
            {
                problems.Add($"outputRoots[{index}].path: '{path}' does not exist.");
            }
            else if (!IsWritable(path))
            {
                problems.Add($"outputRoots[{index}].path: '{path}' cannot be written to.");
            }
        }

        var socketDirectory = Path.GetDirectoryName(configuration.SocketPath!);
        if (!Directory.Exists(socketDirectory))
        {
            problems.Add($"socketPath: '{socketDirectory}' does not exist.");
        }
        else if (
            (File.GetUnixFileMode(socketDirectory!) & UnixFileMode.OtherWrite)
            is not UnixFileMode.None
        )
        {
            problems.Add(
                $"socketPath: anyone may write in '{socketDirectory}', so anyone could replace the socket. Tighten the directory before the driver serves on it."
            );
        }

        return problems.Count is 0
            ? DriverConfigurationResult.Usable(configuration)
            : DriverConfigurationResult.Unusable(problems);
    }

    private static bool IsWritable(string directory)
    {
        var probe = Path.Combine(directory, $".carina-{Guid.NewGuid():N}");
        try
        {
            using (File.Create(probe))
            { }

            File.Delete(probe);

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static DriverConfigurationResult Read(string json)
    {
        DriverConfiguration? configuration;
        try
        {
            configuration = JsonSerializer.Deserialize(
                json,
                DriverConfigurationJsonContext.Default.DriverConfiguration
            );
        }
        catch (JsonException error)
        {
            return Failed($"file: the configuration is not readable JSON: {error.Message}");
        }

        if (configuration is null)
        {
            return Failed("file: the configuration is empty.");
        }

        List<string> problems;
        try
        {
            problems = [.. FindUnknownKeys(json), .. Validate(configuration)];
        }
        catch (Exception error)
        {
            return Failed($"file: the configuration could not be checked: {error.Message}");
        }

        return problems.Count is 0
            ? DriverConfigurationResult.Usable(configuration)
            : DriverConfigurationResult.Unusable(problems);
    }

    private static readonly string[] KnownRootKeys =
    [
        "socketPath",
        "socketGroupId",
        "outputRoots",
        "shutdownGraceHours",
        "liveSessionMinutes",
        "tuner",
        "devices",
    ];

    private static readonly string[] KnownTunerKeys = ["backend"];

    private static readonly string[] KnownOutputRootKeys = ["name", "path"];

    private static readonly string[] KnownDeviceKeys =
    [
        "id",
        "kind",
        "devicePath",
        "enabled",
        "lnbPower",
    ];

    private static IReadOnlyList<string> FindUnknownKeys(string json)
    {
        var problems = new List<string>();

        using var document = JsonDocument.Parse(
            json,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            }
        );

        if (document.RootElement.ValueKind is not JsonValueKind.Object)
        {
            return problems;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!KnownRootKeys.Contains(property.Name, StringComparer.Ordinal))
            {
                problems.Add($"{property.Name}: this driver has no such setting.");
            }
        }

        if (
            document.RootElement.TryGetProperty("tuner", out var tuner)
            && tuner.ValueKind is JsonValueKind.Object
        )
        {
            foreach (var property in tuner.EnumerateObject())
            {
                if (!KnownTunerKeys.Contains(property.Name, StringComparer.Ordinal))
                {
                    problems.Add($"tuner.{property.Name}: this driver has no such setting.");
                }
            }
        }

        CheckArrayKeys(document.RootElement, "devices", KnownDeviceKeys, problems);
        CheckArrayKeys(document.RootElement, "outputRoots", KnownOutputRootKeys, problems);

        return problems;
    }

    private static void CheckArrayKeys(
        JsonElement root,
        string arrayName,
        string[] knownKeys,
        List<string> problems
    )
    {
        if (!root.TryGetProperty(arrayName, out var array))
        {
            return;
        }

        if (array.ValueKind is not JsonValueKind.Array)
        {
            return;
        }

        var index = 0;
        foreach (var entry in array.EnumerateArray())
        {
            if (entry.ValueKind is JsonValueKind.Object)
            {
                foreach (var property in entry.EnumerateObject())
                {
                    if (!knownKeys.Contains(property.Name, StringComparer.Ordinal))
                    {
                        problems.Add(
                            $"{arrayName}[{index}].{property.Name}: this driver has no such setting."
                        );
                    }
                }
            }

            index++;
        }
    }

    private static IReadOnlyList<string> Validate(DriverConfiguration configuration)
    {
        var problems = new List<string>();

        if (!IsUnderRoot(configuration.SocketPath, SocketRoot))
        {
            problems.Add(
                $"socketPath: expected a path under {SocketRoot}, got '{configuration.SocketPath}'."
            );
        }

        if (configuration.SocketGroupId <= 0)
        {
            problems.Add(
                $"socketGroupId: expected the id of the '{DriverConfiguration.SocketGroupName}' group, which is above 0; got {configuration.SocketGroupId}."
            );
        }

        problems.AddRange(OutputRootProblems(configuration));

        if (
            configuration.ShutdownGraceHours is < MinShutdownGraceHours
                or > MaxShutdownGraceHours
        )
        {
            problems.Add(
                $"shutdownGraceHours: expected {MinShutdownGraceHours} to {MaxShutdownGraceHours}, got {configuration.ShutdownGraceHours}."
            );
        }

        if (
            configuration.LiveSessionMinutes is < MinLiveSessionMinutes
                or > MaxLiveSessionMinutes
        )
        {
            problems.Add(
                $"liveSessionMinutes: expected {MinLiveSessionMinutes} to {MaxLiveSessionMinutes}, got {configuration.LiveSessionMinutes}."
            );
        }

        var backend = configuration.Tuner?.Backend ?? TunerBackend.Unspecified;
        if (backend is TunerBackend.Unspecified)
        {
            problems.Add("tuner.backend: expected 'dvb' or 'fake'.");
        }

        var devices = configuration.Devices ?? [];
        if (devices.Count is 0)
        {
            problems.Add("devices: expected at least one device.");
            return problems;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < devices.Count; index++)
        {
            ValidateDevice(devices[index], index, backend, seen, paths, problems);
        }

        if (!devices.Any(device => device?.Enabled is true))
        {
            problems.Add("devices: expected at least one enabled device.");
        }

        return problems;
    }

    private static IReadOnlyList<string> OutputRootProblems(DriverConfiguration configuration)
    {
        var problems = new List<string>();
        var roots = configuration.OutputRoots ?? [];

        if (roots.Count is 0)
        {
            problems.Add(
                "outputRoots: expected at least one named output root for recordings to be written under."
            );

            return problems;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < roots.Count; index++)
        {
            var root = roots[index];

            if (root is null)
            {
                problems.Add($"outputRoots[{index}]: expected an output root, got nothing.");
                continue;
            }

            if (!IsUsableName(root.Name))
            {
                problems.Add(
                    $"outputRoots[{index}].name: expected 1 to {MaxNameLength} characters of A-Z, a-z, 0-9, '-', '_' or '.'; got '{root.Name}'."
                );
            }
            else if (!names.Add(root.Name!))
            {
                problems.Add(
                    $"outputRoots[{index}].name: '{root.Name}' is used by more than one output root."
                );
            }

            if (!IsAbsolutePath(root.Path))
            {
                problems.Add(
                    $"outputRoots[{index}].path: expected an absolute path, got '{root.Path}'."
                );
            }
        }

        return problems;
    }

    private static void ValidateDevice(
        DeviceSettings? device,
        int index,
        TunerBackend backend,
        HashSet<string> seen,
        HashSet<string> paths,
        List<string> problems
    )
    {
        if (device is null)
        {
            problems.Add($"devices[{index}]: expected a device, got nothing.");
            return;
        }

        if (!IsUsableName(device.Id))
        {
            problems.Add(
                $"devices[{index}].id: expected 1 to {MaxNameLength} characters of A-Z, a-z, 0-9, '-', '_' or '.'; got '{device.Id}'."
            );
        }
        else if (!seen.Add(device.Id!))
        {
            problems.Add($"devices[{index}].id: '{device.Id}' is used by more than one device.");
        }

        if (device.Kind is DeviceKind.Unspecified)
        {
            problems.Add(
                $"devices[{index}].kind: expected 'terrestrial' or 'satellite'."
            );
        }

        if (backend is not TunerBackend.Fake)
        {
            if (!IsUnderRoot(device.DevicePath, DeviceRoot))
            {
                problems.Add(
                    $"devices[{index}].devicePath: expected a path under {DeviceRoot} when the backend is 'dvb', got '{device.DevicePath}'."
                );
            }
            else if (!paths.Add(device.DevicePath!))
            {
                problems.Add(
                    $"devices[{index}].devicePath: '{device.DevicePath}' is used by more than one device."
                );
            }
        }

        if (device.LnbPower && device.Kind is not DeviceKind.Satellite)
        {
            problems.Add(
                $"devices[{index}].lnbPower: only a satellite device powers a low-noise block."
            );
        }
    }

    public static bool IsUnderRoot(string? value, string root)
    {
        if (!IsAbsolutePath(value))
        {
            return false;
        }

        var resolved = ResolveLinks(value!);

        return resolved.StartsWith(root, StringComparison.Ordinal)
            && resolved.Length > root.Length;
    }

    private static string ResolveLinks(string value)
    {
        var resolved = LinkTarget(value) ?? Path.GetFullPath(value);
        var trailing = new Stack<string>();

        while (true)
        {
            var parent = Path.GetDirectoryName(resolved);
            if (string.IsNullOrEmpty(parent))
            {
                break;
            }

            var target = LinkTarget(parent);
            trailing.Push(Path.GetFileName(resolved));
            resolved = target ?? parent;

            if (target is null && parent.Length <= 1)
            {
                break;
            }
        }

        return Path.GetFullPath(Path.Combine([resolved, .. trailing]));
    }

    private static string? LinkTarget(string path)
    {
        try
        {
            return Directory.Exists(path)
                ? Directory.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName
                : File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsAbsolutePath(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.StartsWith('/')
        && !value.Contains("..", StringComparison.Ordinal);

    private static bool IsUsableName(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxNameLength)
        {
            return false;
        }

        foreach (var c in value)
        {
            var allowed =
                c is >= 'a' and <= 'z'
                    or >= 'A' and <= 'Z'
                    or >= '0' and <= '9'
                    or '-'
                    or '_'
                    or '.';
            if (!allowed)
            {
                return false;
            }
        }

        return !value.Contains("..", StringComparison.Ordinal);
    }

    private static DriverConfigurationResult Failed(string problem) =>
        DriverConfigurationResult.Unusable([problem]);
}
