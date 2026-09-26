using Carina.Contracts;
using Carina.Domain.Recordings;

namespace Carina.Domain.Channels;

public sealed record TunerFault(TunerDeviceId Tuner, TuneFailureKind Failure)
{
    public string Classification => Failure.ToString();
}

public static class TunerFaults
{
    /// <summary>
    /// Lists, once each and in the driver's order, the tuners the driver has taken out of service
    /// because their frontend would not lock.
    /// </summary>
    public static IReadOnlyList<TunerFault> ThatCannotLock(IReadOnlyList<TunerSnapshot> tuners)
    {
        ArgumentNullException.ThrowIfNull(tuners);

        return
        [
            .. tuners
                .Where(CannotLock)
                .Select(tuner => new TunerFault(new TunerDeviceId(tuner.DeviceId), TuneFailureKind.NoLock))
                .DistinctBy(fault => fault.Tuner),
        ];
    }

    private static bool CannotLock(TunerSnapshot tuner)
        => tuner is { State: TunerState.Faulted, Health.FaultTitle: SessionRefusalTitles.NoLock }
           && WireName.IsUsable(tuner.DeviceId);
}
