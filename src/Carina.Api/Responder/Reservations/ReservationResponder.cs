using Carina.Api.Common;
using Carina.Api.Responder.Epg;
using Carina.Api.Services;
using Carina.Domain.Base;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Api.Responder.Reservations;

public sealed record ReservationProgrammeResponder(
    string Id,
    int NetworkId,
    int ServiceId,
    int EventId,
    DateTime StartsAt,
    string Name,
    string Summary,
    string Extended,
    IReadOnlyList<ProgrammeGenreResponder> Genres,
    DateTime CapturedAt);

public sealed record ReservationWindowResponder(
    DateTime StartAt,
    DateTime EndAt,
    bool EndAtConfirmed,
    int MarginBeforeSeconds,
    int MarginAfterSeconds,
    DateTime EffectiveStartAt,
    DateTime EffectiveEndAt);

public sealed record ReservationDivergedFieldResponder(
    DivergedField Field,
    string? Before,
    string? After,
    DateTime DetectedAt)
{
    public static ReservationDivergedFieldResponder Of(EpgDivergence divergence)
    {
        ArgumentNullException.ThrowIfNull(divergence);

        return new ReservationDivergedFieldResponder(
            divergence.Field,
            divergence.Before,
            divergence.After,
            divergence.DetectedAt);
    }
}

public sealed record ReservationDivergenceResponder(
    bool Diverged,
    IReadOnlyList<ReservationDivergedFieldResponder> Detail,
    bool ProgrammeMissing,
    DateTime? AcknowledgedAt);

public sealed record ReservationReceptionResponder(bool Unavailable, DateTime? Since);

public sealed record ReservationBroadcastGroupResponder(string? Key, BroadcastGroupRole Role);

public sealed record ReservationResponder(
    Guid Id,
    ReservationProgrammeResponder Programme,
    ReservationOrigin Origin,
    Guid? RuleId,
    int Priority,
    ReservationWindowResponder Window,
    ReservationState State,
    ReservationStanding Standing,
    DateTime? StartedAt,
    RecordingOutcome? RecordingOutcome,
    ReservationReceptionResponder Reception,
    ReservationDivergenceResponder Epg,
    ReservationBroadcastGroupResponder BroadcastGroup,
    DateTime CreatedAt)
{
    public static ReservationResponder Of(Reservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        return new ReservationResponder(
            reservation.Id.Value,
            new ReservationProgrammeResponder(
                ProgrammeIdText.Of(reservation.Programme.Id),
                reservation.NetworkId.Value,
                reservation.ServiceId.Value,
                reservation.EventId.Value,
                reservation.ProgrammeStartsAt,
                reservation.SnapshotName,
                reservation.SnapshotSummary,
                reservation.SnapshotExtended,
                [.. reservation.SnapshotGenres.Select(ProgrammeGenreResponder.Of)],
                reservation.CapturedAt),
            reservation.IsRuleBorn ? ReservationOrigin.ByRule : ReservationOrigin.ByHand,
            reservation.RuleId?.Value,
            reservation.Priority.Value,
            new ReservationWindowResponder(
                reservation.StartAt,
                reservation.EndAt,
                reservation.EndAtConfirmed,
                reservation.MarginBefore.Seconds,
                reservation.MarginAfter.Seconds,
                reservation.EffectiveStartAt,
                reservation.EffectiveEndAt),
            reservation.State,
            reservation.Standing,
            reservation.StartedAt,
            reservation.RecordingOutcome,
            new ReservationReceptionResponder(
                reservation.ReceptionUnavailable,
                reservation.ReceptionUnavailableSince),
            new ReservationDivergenceResponder(
                reservation.EpgDiverged,
                [.. reservation.EpgDivergences.Select(ReservationDivergedFieldResponder.Of)],
                reservation.EpgMissing,
                reservation.AcknowledgedAt),
            new ReservationBroadcastGroupResponder(
                reservation.BroadcastGroupKey?.Value,
                reservation.BroadcastGroupRole),
            reservation.CreatedAt);
    }
}

public sealed record ReservationListResponder(
    IReadOnlyList<ReservationResponder> Items,
    int Total,
    int CurrentPage,
    int LastPage,
    int PerPage)
{
    public static ReservationListResponder Of(PaginatedList<Reservation> found)
    {
        ArgumentNullException.ThrowIfNull(found);

        return new ReservationListResponder(
            [.. found.Items.Select(ReservationResponder.Of)],
            found.Total,
            found.CurrentPage,
            found.LastPage,
            found.PerPage);
    }
}

public sealed record ReservationSettlementResponder(
    ReservationResponder Reservation,
    AllocationVerdict? Verdict,
    IReadOnlyList<ReservationResponder> Instead,
    int SeatsLeftOut)
{
    public static ReservationSettlementResponder Of(ReservationSettlement settlement)
    {
        ArgumentNullException.ThrowIfNull(settlement);

        return new ReservationSettlementResponder(
            ReservationResponder.Of(settlement.Reservation),
            settlement.Verdict,
            [.. settlement.Instead.Select(ReservationResponder.Of)],
            settlement.SeatsLeftOut);
    }
}
