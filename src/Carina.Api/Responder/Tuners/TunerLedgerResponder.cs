using Carina.Api.Responder.Scans;
using Carina.Api.Services;
using Carina.Contracts;

namespace Carina.Api.Responder.Tuners;

public sealed record TunerEntryResponder(string DeviceId, bool Disabled, bool LnbPower, TunerKind Kind)
{
    public static TunerEntryResponder Of(TunerConfigEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new TunerEntryResponder(entry.DeviceId, entry.Disabled, entry.LnbPower, entry.Kind);
    }
}

public sealed record TunerObservationResponder(
    string DeviceId,
    TunerKind Kind,
    TunerState State,
    string? Detail,
    TunerHealthLevel Health,
    bool DisablePending,
    bool LnbPowered,
    string? HealthDetail,
    DateTimeOffset? HealthChangedAt,
    TunerFaultKind FaultKind,
    TunerKind? FaultDeclaredKind,
    IReadOnlyList<TunerKind> FaultReceivableKinds,
    TunerDegradedKind DegradedKind,
    string? SessionId,
    SessionPurpose SessionPurpose,
    DateTimeOffset? SessionStartedAt,
    DateTimeOffset? SessionEndsAt,
    ScanTargetResponder? SessionTuning,
    string? SessionTitle,
    string? Machine,
    TunerHardwareResponder? Hardware)
{
    public static TunerObservationResponder Of(TunerSnapshot snapshot, string? sessionTitle = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        TunerHealthDto? health = snapshot.Health;
        CurrentSessionDto? session = snapshot.CurrentSession;

        return new TunerObservationResponder(
            snapshot.DeviceId,
            snapshot.Kind,
            snapshot.State,
            snapshot.Detail,
            health?.Level ?? TunerHealthLevel.Unspecified,
            health?.DisablePending ?? false,
            health?.LnbPowered ?? false,
            health?.Detail,
            health?.ChangedAt,
            health?.FaultKind ?? TunerFaultKind.Unspecified,
            health?.FaultDeclaredKind,
            health?.FaultReceivableKinds ?? [],
            health?.DegradedKind ?? TunerDegradedKind.Unspecified,
            session is null ? null : session.SessionId.ToString(),
            session?.Purpose ?? SessionPurpose.Unspecified,
            session?.StartedAt,
            session?.EndsAt,
            ScanTargetResponder.Of(session?.Tune),
            sessionTitle,
            snapshot.Machine,
            TunerHardwareResponder.Of(snapshot.Hardware));
    }
}

public sealed record TunerHardwareResponder(
    string? Name,
    string? KernelDriver,
    TunerBus Bus,
    string? DevicePath,
    string? UsbPort,
    bool? Present,
    DateTimeOffset? LastSeenAt)
{
    public static TunerHardwareResponder? Of(TunerHardwareDto? hardware) =>
        hardware is null
            ? null
            : new TunerHardwareResponder(
                hardware.Name,
                hardware.KernelDriver,
                hardware.Bus,
                hardware.DevicePath,
                hardware.UsbPort,
                hardware.Present,
                hardware.LastSeenAt);
}

public sealed record TunerLedgerResponder(
    IReadOnlyList<TunerEntryResponder> Desired,
    string? SavedHash,
    string? LoadedHash,
    bool Drifted,
    IReadOnlyList<TunerObservationResponder>? Observed,
    DateTimeOffset? ObservedAt,
    string? ObservationFailure)
{
    public static TunerLedgerResponder Of(TunerLedgerView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new TunerLedgerResponder(
            [.. view.Desired.Select(TunerEntryResponder.Of)],
            view.SavedHash,
            view.LoadedHash,
            view.Drifted,
            view.Observed is null
                ? null
                : [
                    .. view.Observed.Tuners.Select(tuner => TunerObservationResponder.Of(
                        tuner,
                        TitleOf(tuner, view.Observed.SessionTitles))),
                ],
            view.Observed?.ObservedAt,
            view.ObservationFailure);
    }

    private static string? TitleOf(TunerSnapshot tuner, IReadOnlyDictionary<SessionId, string> titles) =>
        tuner.CurrentSession is { } session && titles.TryGetValue(session.SessionId, out string? title) ? title : null;
}
