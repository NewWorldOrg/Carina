using System.Text.Json;

namespace Carina.Driver.Configuration;

public sealed record DriverConfigurationResult(
    DriverConfiguration? Configuration,
    IReadOnlyList<string> Problems
);

public static class DriverConfigurationReader
{
    private const int MinShutdownGraceHours = 1;
    private const int MaxShutdownGraceHours = 168;
    private const int MaxDeviceIdLength = 64;

    public static DriverConfigurationResult ReadFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Failed("file: no configuration path was given.");
        }

        if (!File.Exists(path))
        {
            return Failed($"file: no configuration at '{path}'.");
        }

        try
        {
            return Read(File.ReadAllText(path));
        }
        catch (IOException error)
        {
            return Failed($"file: '{path}' could not be read: {error.Message}");
        }
        catch (UnauthorizedAccessException error)
        {
            return Failed($"file: '{path}' could not be read: {error.Message}");
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

        var problems = Validate(configuration);

        return problems.Count is 0
            ? new DriverConfigurationResult(configuration, problems)
            : new DriverConfigurationResult(null, problems);
    }

    private static IReadOnlyList<string> Validate(DriverConfiguration configuration)
    {
        var problems = new List<string>();

        if (!IsAbsolutePath(configuration.SocketPath))
        {
            problems.Add(
                $"socketPath: expected an absolute path, got '{configuration.SocketPath}'."
            );
        }

        if (!IsAbsolutePath(configuration.RecordingsDirectory))
        {
            problems.Add(
                $"recordingsDirectory: expected an absolute path, got '{configuration.RecordingsDirectory}'."
            );
        }

        if (
            configuration.ShutdownGraceHours is < MinShutdownGraceHours
                or > MaxShutdownGraceHours
        )
        {
            problems.Add(
                $"shutdownGraceHours: expected {MinShutdownGraceHours} to {MaxShutdownGraceHours}, got {configuration.ShutdownGraceHours}."
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
        for (var index = 0; index < devices.Count; index++)
        {
            ValidateDevice(devices[index], index, backend, seen, problems);
        }

        return problems;
    }

    private static void ValidateDevice(
        DeviceSettings device,
        int index,
        TunerBackend backend,
        HashSet<string> seen,
        List<string> problems
    )
    {
        if (!IsUsableDeviceName(device.Id))
        {
            problems.Add(
                $"devices[{index}].id: expected 1 to {MaxDeviceIdLength} characters of A-Z, a-z, 0-9, '-', '_' or '.'; got '{device.Id}'."
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

        if (backend is TunerBackend.Dvb && !IsAbsolutePath(device.DevicePath))
        {
            problems.Add(
                $"devices[{index}].devicePath: expected an absolute path when the backend is 'dvb', got '{device.DevicePath}'."
            );
        }
    }

    private static bool IsAbsolutePath(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.StartsWith('/')
        && !value.Contains("..", StringComparison.Ordinal);

    private static bool IsUsableDeviceName(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxDeviceIdLength)
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

    private static DriverConfigurationResult Failed(string problem) => new(null, [problem]);
}
