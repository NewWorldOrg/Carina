using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Carina.Driver.Configuration;

[JsonConverter(typeof(TunerBackendConverter))]
public enum TunerBackend
{
    Unspecified = 0,

    Dvb = 1,

    Fake = 2,

    Usb = 3,
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

public sealed record LendSettings(
    string? Address,
    int Port = CardLendingSettings.DefaultPort,
    IReadOnlyList<string>? HostNames = null,
    int HopLimit = CardLendingSettings.DefaultHopLimit,
    int RoundTripMilliseconds = CardLendingSettings.DefaultRoundTripMilliseconds,
    int Leases = CardLendingSettings.DefaultLeases,
    string? KeyFile = null
);

public sealed record BorrowSettings(
    string? Address,
    int Port = CardLendingSettings.DefaultPort,
    string? HostName = null,
    int HopLimit = CardLendingSettings.DefaultHopLimit,
    string? KeyFile = null
);

/// <summary>
/// Lending this driver's card to other drivers in the household, or borrowing one; absent, the driver does neither.
/// </summary>
public sealed record CardLendingSettings(LendSettings? Lend = null, BorrowSettings? Borrow = null)
{
    public const int DefaultPort = 40772;

    public const int DefaultHopLimit = 3;

    public const int DefaultLeases = 4;

    public const int DefaultRoundTripMilliseconds = 5;
}

public sealed record DriverConfiguration(
    string? SocketPath = DriverConfiguration.DefaultSocketPath,
    IReadOnlyList<OutputRootSettings>? OutputRoots = null,
    int ShutdownGraceHours = DriverConfiguration.DefaultShutdownGraceHours,
    TunerSettings? Tuner = null,
    IReadOnlyList<DeviceSettings>? Devices = null,
    int SocketGroupId = DriverConfiguration.DefaultSocketGroupId,
    int LiveSessionMinutes = DriverConfiguration.DefaultLiveSessionMinutes,
    int WalkSessionMinutes = DriverConfiguration.DefaultWalkSessionMinutes,
    CardLendingSettings? CardLending = null
)
{
    public const string SocketGroupName = "carina";

    public const string DefaultSocketPath = "/run/carina/driver.sock";

    public const int DefaultShutdownGraceHours = 6;

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
