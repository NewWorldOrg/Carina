using Carina.Api.Services;
using Carina.Contracts;

namespace Carina.Api.Responder.Tuners;

public sealed record TunerEntryResponder(string DeviceId, bool Disabled, bool LnbPower)
{
    public static TunerEntryResponder Of(TunerConfigEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new TunerEntryResponder(entry.DeviceId, entry.Disabled, entry.LnbPower);
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
    string? SessionId,
    SessionPurpose SessionPurpose)
{
    public static TunerObservationResponder Of(TunerSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var health = snapshot.Health;
        var session = snapshot.CurrentSession;

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
            session is null ? null : session.SessionId.ToString(),
            session?.Purpose ?? SessionPurpose.Unspecified);
    }
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
                : [.. view.Observed.Tuners.Select(TunerObservationResponder.Of)],
            view.Observed?.ObservedAt,
            view.ObservationFailure);
    }
}
