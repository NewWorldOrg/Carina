using Carina.Contracts;
using Carina.Driver.Configuration;

namespace Carina.Driver.Sessions;

/// <summary>
/// Why a device was faulted, and for a device the ledger disagrees with, the kind the ledger declares and
/// the kinds the device receives.
/// </summary>
public sealed record DeviceFaultKind
{
    private DeviceFaultKind(TunerFaultKind kind, DeviceKind? declared, IReadOnlyList<DeviceKind> receives)
    {
        Kind = kind;
        Declared = declared;
        Receives = receives;
    }

    public static DeviceFaultKind Failed { get; } = new(TunerFaultKind.DeviceFailed, null, []);

    public static DeviceFaultKind FailedAgain { get; } = new(TunerFaultKind.DeviceFailedAgain, null, []);

    public static DeviceFaultKind RepeatedTuneFailure { get; } = new(TunerFaultKind.RepeatedTuneFailure, null, []);

    public TunerFaultKind Kind { get; }

    public DeviceKind? Declared { get; }

    public IReadOnlyList<DeviceKind> Receives { get; }

    public static DeviceFaultKind Disagreeing(DeviceKind declared, IReadOnlyList<DeviceKind> receives)
    {
        ArgumentNullException.ThrowIfNull(receives);

        return new DeviceFaultKind(TunerFaultKind.LedgerDisagrees, declared, [.. receives]);
    }
}
