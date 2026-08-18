using System.Diagnostics.CodeAnalysis;
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

public sealed record TunerSettings(
    TunerBackend Backend,
    int SignalQualitySeconds = TunerSettings.DefaultSignalQualitySeconds,
    int DemuxBufferBytes = TunerSettings.DefaultDemuxBufferBytes
)
{
    public const int DefaultSignalQualitySeconds = 10;

    public const int DefaultDemuxBufferBytes = 16 * 1024 * 1024;

    [JsonIgnore]
    public TimeSpan SignalQualityInterval => TimeSpan.FromSeconds(SignalQualitySeconds);
}

public sealed record DeviceSettings(
    string? Id,
    DeviceKind Kind,
    string? DevicePath = null,
    bool Enabled = true,
    bool LnbPower = false
);

public sealed record OutputRootSettings(string? Name, string? Path);

public sealed record DriverConfiguration(
    string? SocketPath,
    IReadOnlyList<OutputRootSettings>? OutputRoots,
    int ShutdownGraceHours,
    TunerSettings? Tuner,
    IReadOnlyList<DeviceSettings>? Devices,
    int SocketGroupId = DriverConfiguration.DefaultSocketGroupId,
    int LiveSessionMinutes = DriverConfiguration.DefaultLiveSessionMinutes,
    int WalkSessionMinutes = DriverConfiguration.DefaultWalkSessionMinutes
)
{
    public const string SocketGroupName = "carina";

    public const int DefaultSocketGroupId = 10001;

    public const int DefaultLiveSessionMinutes = 240;

    public const int DefaultWalkSessionMinutes = 30;

    public bool TryResolveOutputRoot(string? name, [NotNullWhen(true)] out string? path)
    {
        path = null;

        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        foreach (OutputRootSettings root in OutputRoots ?? [])
        {
            if (root?.Name is null || !string.Equals(root.Name, name, StringComparison.Ordinal))
            {
                continue;
            }

            path = root.Path;

            return path is not null;
        }

        return false;
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    AllowTrailingCommas = true,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    WriteIndented = true
)]
[JsonSerializable(typeof(DriverConfiguration))]
internal sealed partial class DriverConfigurationJsonContext : JsonSerializerContext;
