using Carina.Contracts;
using Carina.Domain.Recordings;

namespace Carina.Domain.Channels;

/// <summary>
/// What the driver says is wrong with a tuner it lists: taken out of service, and why, or still handed out but
/// not quite well, and why.
/// </summary>
public enum TunerTroubleKind
{
    NoLock = 1,

    RepeatedTuneFailure = 2,

    LedgerDisagrees = 3,

    DeviceFailed = 4,

    DeviceFailedAgain = 5,

    Faulted = 6,

    TuneFailing = 7,

    Degraded = 8,
}

public sealed record TunerTrouble(TunerDeviceId Tuner, TunerTroubleKind Kind)
{
    public string Classification => Kind.ToString();
}

public static class TunerTroubles
{
    public const string CannotLockClassification = nameof(TunerTroubleKind.NoLock);

    /// <summary>
    /// Lists, once each and in the driver's order, the enabled tuners the driver says are out of service or not
    /// quite well, each with the kind of trouble it names.
    /// </summary>
    public static IReadOnlyList<TunerTrouble> Of(IReadOnlyList<TunerSnapshot> tuners)
    {
        ArgumentNullException.ThrowIfNull(tuners);

        return
        [
            .. tuners
                .Where(tuner => tuner.State is not TunerState.Disabled && WireName.IsUsable(tuner.DeviceId))
                .Select(tuner => Kind(tuner) is { } kind ? new TunerTrouble(new TunerDeviceId(tuner.DeviceId), kind) : null)
                .OfType<TunerTrouble>()
                .DistinctBy(trouble => trouble.Tuner),
        ];
    }

    /// <summary>
    /// Reads the kind of trouble an incident's classification names, or <see langword="null"/> when it names none.
    /// </summary>
    public static TunerTroubleKind? Named(string? classification)
        => classification is not null
           && Enum.TryParse(classification, ignoreCase: false, out TunerTroubleKind kind)
           && Enum.IsDefined(kind)
           && string.Equals(kind.ToString(), classification, StringComparison.Ordinal)
            ? kind
            : null;

    /// <summary>
    /// Whether a kind of trouble is one the driver takes the tuner out of service for.
    /// </summary>
    public static bool TakesItOutOfService(TunerTroubleKind kind)
        => kind is not (TunerTroubleKind.TuneFailing or TunerTroubleKind.Degraded);

    private static TunerTroubleKind? Kind(TunerSnapshot tuner)
    {
        if (tuner.State is TunerState.Faulted || tuner.Health?.Level is TunerHealthLevel.Faulted)
        {
            return tuner.Health?.FaultTitle == SessionRefusalTitles.NoLock
                ? TunerTroubleKind.NoLock
                : Faulted(tuner.Health?.FaultKind ?? TunerFaultKind.Unspecified);
        }

        return tuner.Health?.Level is TunerHealthLevel.Degraded
            ? tuner.Health.DegradedKind is TunerDegradedKind.TuneFailing
                ? TunerTroubleKind.TuneFailing
                : TunerTroubleKind.Degraded
            : null;
    }

    private static TunerTroubleKind Faulted(TunerFaultKind kind) => kind switch
    {
        TunerFaultKind.RepeatedTuneFailure => TunerTroubleKind.RepeatedTuneFailure,
        TunerFaultKind.LedgerDisagrees => TunerTroubleKind.LedgerDisagrees,
        TunerFaultKind.DeviceFailed => TunerTroubleKind.DeviceFailed,
        TunerFaultKind.DeviceFailedAgain => TunerTroubleKind.DeviceFailedAgain,
        _ => TunerTroubleKind.Faulted,
    };
}
