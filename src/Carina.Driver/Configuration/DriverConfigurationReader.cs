using System.Text.Json;

namespace Carina.Driver.Configuration;

/// <summary>
/// A configuration, or the reasons there is not one.
/// </summary>
/// <param name="Configuration">The settings, when every check passed.</param>
/// <param name="Problems">Everything wrong, each naming its setting.</param>
public sealed record DriverConfigurationResult(
    DriverConfiguration? Configuration,
    IReadOnlyList<string> Problems
);

/// <summary>
/// Reads the driver's configuration file and says whether it is usable.
/// </summary>
/// <remarks>
/// This runs before the socket is bound and before a device is opened, so that a
/// mistake in the file costs a message and an exit code rather than a half-started
/// process holding a tuner. Every problem is collected: failing on the first one
/// makes the operator restart once per typo.
///
/// Nothing here throws for bad input. A malformed file is a finding like any other,
/// because the caller's job is to print findings and exit, not to catch.
/// </remarks>
public static class DriverConfigurationReader
{
    private const int MinShutdownGraceHours = 1;
    private const int MaxShutdownGraceHours = 168;
    private const int MaxDeviceIdLength = 64;

    /// <summary>Reads the file at <paramref name="path"/>.</summary>
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

    /// <summary>Reads a configuration document that is already in hand.</summary>
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

        // The synthetic backend has no device nodes, so a path there would be a
        // value nothing could open.
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
