using System.Text.Json.Serialization;

namespace Carina.Driver.Configuration;

[JsonConverter(typeof(TunerBackendConverter))]
public enum TunerBackend
{
    Unspecified = 0,

    Dvb = 1,

    Fake = 2,
}

[JsonConverter(typeof(DeviceKindConverter))]
public enum DeviceKind
{
    Unspecified = 0,

    Terrestrial = 1,

    Satellite = 2,
}

public sealed record TunerSettings(TunerBackend Backend);

public sealed record DeviceSettings(
    string? Id,
    DeviceKind Kind,
    string? DevicePath = null,
    bool Enabled = true,
    bool LnbPower = false
);

public sealed record DriverConfiguration(
    string? SocketPath,
    string? RecordingsDirectory,
    int ShutdownGraceHours,
    TunerSettings? Tuner,
    IReadOnlyList<DeviceSettings>? Devices
);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    AllowTrailingCommas = true,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip
)]
[JsonSerializable(typeof(DriverConfiguration))]
internal sealed partial class DriverConfigurationJsonContext : JsonSerializerContext;
