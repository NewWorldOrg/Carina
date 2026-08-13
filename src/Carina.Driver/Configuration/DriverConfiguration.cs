using System.Text.Json.Serialization;

namespace Carina.Driver.Configuration;

/// <summary>Where the driver gets its transport stream from.</summary>
[JsonConverter(typeof(TunerBackendConverter))]
public enum TunerBackend
{
    /// <summary>Not stated, or stated in a spelling this build does not know.</summary>
    Unspecified = 0,

    /// <summary>Real hardware, through the Linux DVB interface.</summary>
    Dvb = 1,

    /// <summary>Synthetic streams, so that development and CI need no hardware.</summary>
    Fake = 2,
}

/// <summary>Which side of the hardware a device serves.</summary>
[JsonConverter(typeof(DeviceKindConverter))]
public enum DeviceKind
{
    /// <summary>Not stated, or stated in a spelling this build does not know.</summary>
    Unspecified = 0,

    /// <summary>Terrestrial.</summary>
    Terrestrial = 1,

    /// <summary>Satellite.</summary>
    Satellite = 2,
}

/// <summary>Which backend serves every device.</summary>
/// <param name="Backend">Hardware or synthetic.</param>
public sealed record TunerSettings(TunerBackend Backend);

/// <summary>
/// One tuner device, as the operator wrote it down.
/// </summary>
/// <param name="Id">The name the rest of the system uses for this device.</param>
/// <param name="Kind">Which side of the hardware it serves.</param>
/// <param name="DevicePath">The device node, when there is hardware behind it.</param>
/// <param name="Enabled">Whether the driver may allocate it.</param>
/// <param name="LnbPower">Whether the driver powers the low-noise block.</param>
public sealed record DeviceSettings(
    string? Id,
    DeviceKind Kind,
    string? DevicePath = null,
    bool Enabled = true,
    bool LnbPower = false
);

/// <summary>
/// Everything the driver reads once, at startup.
/// </summary>
/// <param name="SocketPath">Where the Unix socket goes.</param>
/// <param name="RecordingsDirectory">Where recordings are written.</param>
/// <param name="ShutdownGraceHours">How long a shutdown waits for recordings to finish.</param>
/// <param name="Tuner">Which backend serves every device.</param>
/// <param name="Devices">The devices this driver owns.</param>
/// <remarks>
/// There is no setting here that names a port or a URL. The socket is the only way
/// in, and leaving that to construction rather than to configuration means no
/// deployment can open a second door by editing a file.
///
/// The values are read once. Nothing reloads them: a driver that changed its output
/// directory while a recording was running would write the rest of that recording
/// somewhere else.
/// </remarks>
public sealed record DriverConfiguration(
    string? SocketPath,
    string? RecordingsDirectory,
    int ShutdownGraceHours,
    TunerSettings? Tuner,
    IReadOnlyList<DeviceSettings>? Devices
);

/// <summary>Generated serialisation metadata for the driver's configuration file.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    AllowTrailingCommas = true,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip
)]
[JsonSerializable(typeof(DriverConfiguration))]
internal sealed partial class DriverConfigurationJsonContext : JsonSerializerContext;
