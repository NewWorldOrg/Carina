using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Recordings;

public sealed class RecordingRefusalReporter(
    IReservationRepository reservations,
    IReservationOutcomeRepository outcomes,
    ITuneFailureReporter tuning,
    ILogger<RecordingRefusalReporter> logger)
{
    public async Task ReportAsync(
        IReadOnlyList<RecordingRefusal> refused,
        DateTime at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(refused);

        foreach (RecordingRefusal refusal in refused)
        {
            if (refusal.Reported is { } failure)
            {
                await TellAsync(refusal, failure, at, cancellationToken);
            }
        }
    }

    private async Task TellAsync(
        RecordingRefusal refusal,
        RecordingStartFailure failure,
        DateTime at,
        CancellationToken cancellationToken)
    {
        if (failure.IsWorthReportingToTheTuner && refusal.TunedWith is { } candidate)
        {
            await tuning.ReportFailureAsync(candidate, at, cancellationToken);
        }

        if (await reservations.FindAsync(refusal.Reservation, cancellationToken) is not { } reservation)
        {
            logger.LogWarning(
                "A start refused as {Fault} belongs to a reservation that is gone, so the ledger has nowhere to "
                + "write it down.",
                failure.Fault);

            return;
        }

        IReadOnlyList<ReservationOutcome> already = await outcomes.ListForReservationAsync(
            reservation.Id,
            cancellationToken);

        if (already.Any(one => one.Kind == failure.Kind))
        {
            return;
        }

        await outcomes.AddAsync(
            ReservationOutcome.Record(
                ReservationOutcomeId.New(),
                reservation,
                failure.Kind,
                failure.TuneFailure,
                null,
                [failure.Fault],
                failure.Kind is ReservationOutcomeKind.Competing ? refusal.RecordedInstead : [],
                at),
            cancellationToken);
    }
}
