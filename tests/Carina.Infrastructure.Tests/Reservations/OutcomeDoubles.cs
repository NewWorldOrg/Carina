using Carina.Domain.Base;
using Carina.Domain.Reservations;

namespace Carina.Infrastructure.Tests.Reservations;

internal sealed class HeldOutcomes(WatchedWrite? write = null) : IReservationOutcomeRepository
{
    private readonly List<ReservationOutcome> held = [];

    public IReadOnlyList<ReservationOutcome> Held => held;

    public List<string> WroteOutsideAWrite { get; } = [];

    public Exception? Throws { get; set; }

    public Task AddAsync(ReservationOutcome outcome, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        if (Throws is { } refusal)
        {
            throw refusal;
        }

        if (write is { Open: false })
        {
            WroteOutsideAWrite.Add($"record {outcome.ReservationId.Value} {outcome.Kind}");
        }

        held.Add(outcome);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ReservationOutcome>> ListAsync(
        OutcomeSpan span,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(span);

        return Task.FromResult<IReadOnlyList<ReservationOutcome>>(
        [
            .. held
                .Where(outcome => outcome.OccurredAt >= span.From && outcome.OccurredAt <= span.To)
                .Where(outcome => span.Kind is null || outcome.Kind == span.Kind)
                .OrderBy(outcome => outcome.OccurredAt),
        ]);
    }

    public Task<PaginatedList<ReservationOutcome>> ListAsync(
        ReservationOutcomeQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        ReservationOutcome[] ordered =
        [
            .. held
                .OrderByDescending(outcome => outcome.OccurredAt)
                .ThenBy(outcome => outcome.Id.Value),
        ];

        return Task.FromResult(new PaginatedList<ReservationOutcome>(
            [.. ordered.Skip((query.Page - 1) * query.PerPage).Take(query.PerPage)],
            ordered.Length,
            query.Page,
            query.PerPage));
    }

    public Task<IReadOnlyList<ReservationOutcome>> ListForReservationAsync(
        ReservationId reservationId,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ReservationOutcome>>(
            [.. held.Where(outcome => outcome.ReservationId.Equals(reservationId))]);
}
