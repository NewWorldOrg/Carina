using Carina.Api.Common;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Api.Responder.Reservations;

public sealed record ReservationOutcomeProgrammeResponder(
    string Id,
    int NetworkId,
    int ServiceId,
    int EventId,
    DateTime StartsAt,
    string Name);

/// <summary>
/// One line of the ledger, as the ledger wrote it. <c>TuneFailure</c> and <c>RecordingOutcome</c>
/// are null whenever the ledger holds nothing there: an answer is never filled in from what the
/// classification would make likely.
/// </summary>
public sealed record ReservationOutcomeResponder(
    Guid Id,
    Guid ReservationId,
    ReservationOutcomeProgrammeResponder Programme,
    ReservationOutcomeKind Kind,
    TuneFailureKind? TuneFailure,
    RecordingOutcome? RecordingOutcome,
    IReadOnlyList<Guid> RecordedInstead,
    DateTime EffectiveStartAt,
    DateTime EffectiveEndAt,
    int Priority,
    Guid? RuleId,
    DateTime OccurredAt)
{
    public static ReservationOutcomeResponder Of(ReservationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return new ReservationOutcomeResponder(
            outcome.Id.Value,
            outcome.ReservationId.Value,
            new ReservationOutcomeProgrammeResponder(
                ProgrammeIdText.Of(new ProgrammeId(outcome.NetworkId, outcome.ServiceId, outcome.EventId)),
                outcome.NetworkId.Value,
                outcome.ServiceId.Value,
                outcome.EventId.Value,
                outcome.ProgrammeStartsAt,
                outcome.SnapshotName),
            outcome.Kind,
            outcome.TuneFailure,
            outcome.RecordingOutcome,
            outcome.RecordedInstead,
            outcome.EffectiveStartAt,
            outcome.EffectiveEndAt,
            outcome.Priority.Value,
            outcome.RuleId?.Value,
            outcome.OccurredAt);
    }
}

public sealed record ReservationOutcomeListResponder(
    IReadOnlyList<ReservationOutcomeResponder> Items,
    int Total,
    int CurrentPage,
    int LastPage,
    int PerPage)
{
    public static ReservationOutcomeListResponder Of(PaginatedList<ReservationOutcome> found)
    {
        ArgumentNullException.ThrowIfNull(found);

        return new ReservationOutcomeListResponder(
            [.. found.Items.Select(ReservationOutcomeResponder.Of)],
            found.Total,
            found.CurrentPage,
            found.LastPage,
            found.PerPage);
    }
}
