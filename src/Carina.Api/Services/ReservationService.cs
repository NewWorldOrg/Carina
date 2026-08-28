using Carina.Api.Common;
using Carina.Domain.Base;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Reservations;

using Microsoft.EntityFrameworkCore;

namespace Carina.Api.Services;

public enum ReservationFailure
{
    NoSuchReservation = 1,

    NoSuchProgramme = 2,

    ProgrammeIsAShadow = 3,

    AlreadyReserved = 4,

    AlreadyRecording = 5,

    NotStanding = 6,

    NotCancelled = 7,

    AlreadyOver = 8,

    TunersCannotBeCounted = 9,

    SomethingArrivedWhileReading = 10,

    TurningIntoARecording = 11,

    RecordingCameOfIt = 12,
}

public sealed record ReservationSettlement(
    Reservation Reservation,
    AllocationVerdict? Verdict,
    IReadOnlyList<Reservation> Instead,
    int SeatsLeftOut);

public sealed record ReservationDiscarded(ReservationId Id);

public sealed record ReservationDraft(
    ProgrammeId Programme,
    DateTime ProgrammeStartsAt,
    Priority Priority,
    Margin MarginBefore,
    Margin MarginAfter);

public sealed class ReservationService(
    IReservationRepository reservations,
    IProgrammeRepository programmes,
    ReservationSchedulingService scheduler,
    TimeProvider clock)
{
    public async Task<ServiceResult<PaginatedList<Reservation>>> ListAsync(
        ReservationQuery query,
        CancellationToken cancellationToken)
        => ServiceResult<PaginatedList<Reservation>>.Success(
            await reservations.ListAsync(query, cancellationToken));

    public async Task<ServiceResult<Reservation, ReservationFailure>> FindAsync(
        ReservationId id,
        CancellationToken cancellationToken)
        => await reservations.FindAsync(id, cancellationToken) is { } reservation
            ? ServiceResult<Reservation, ReservationFailure>.Success(reservation)
            : Missing<Reservation>(id);

    public async Task<ServiceResult<ReservationSettlement, ReservationFailure>> CreateAsync(
        ReservationDraft draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (await programmes.FindAsync(draft.Programme, cancellationToken) is not { } programme
            || programme.StartsAt != draft.ProgrammeStartsAt)
        {
            return ServiceResult<ReservationSettlement, ReservationFailure>.Failure(
                $"The programme guide holds no {ProgrammeIdText.Of(draft.Programme)} starting at "
                + $"{draft.ProgrammeStartsAt:O}. An event id is reused, so the start it was asked for is part of "
                + "naming the broadcast rather than a detail beside it.",
                ReservationFailure.NoSuchProgramme);
        }

        if (programme.IsShadow)
        {
            return ServiceResult<ReservationSettlement, ReservationFailure>.Failure(
                $"Programme {ProgrammeIdText.Of(draft.Programme)} is carried as a shadow of a broadcast that "
                + "belongs to another service, so it is not the entry to record.",
                ReservationFailure.ProgrammeIsAShadow);
        }

        var reference = new ProgrammeRef(
            programme.NetworkId,
            programme.ServiceId,
            programme.EventId,
            programme.StartsAt);

        if (await reservations.FindByProgrammeAsync(reference, cancellationToken) is { } already)
        {
            return AlreadyReserved<ReservationSettlement>(already);
        }

        DateTime at = clock.GetUtcNow().UtcDateTime;
        Reservation planned = Reservation.Plan(
            ReservationId.New(),
            reference,
            null,
            draft.Priority,
            programme.StartsAt,
            programme.EndsAt ?? programme.StartsAt + Reservation.ProvisionalLengthWhenTheEndIsNotAnnounced,
            programme.EndsAt is not null,
            draft.MarginBefore,
            draft.MarginAfter,
            Snapshot(programme, at),
            null,
            BroadcastGroupRole.Standalone,
            at);

        SchedulingRun run;

        try
        {
            run = await scheduler.CreateAsync(planned, cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await reservations.FindByProgrammeAsync(reference, cancellationToken) is not { } raced)
            {
                throw;
            }

            return AlreadyReserved<ReservationSettlement>(raced);
        }

        return await SettledAsync(run, planned, cancellationToken);
    }

    public async Task<ServiceResult<ReservationSettlement, ReservationFailure>> ReviseAsync(
        ReservationId id,
        ReservationRevision revision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(revision);

        if (await reservations.FindAsync(id, cancellationToken) is not { } reservation)
        {
            return Missing<ReservationSettlement>(id);
        }

        if (Refuses(reservation, revision.Move) is { } refusal)
        {
            return refusal;
        }

        SchedulingRun run = await scheduler.ReviseAsync(reservation, revision, cancellationToken);

        return await SettledAsync(run, reservation, cancellationToken);
    }

    public async Task<ServiceResult<ReservationDiscarded, ReservationFailure>> DiscardAsync(
        ReservationId id,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        return await reservations.DiscardAsync(id, cancellationToken) switch
        {
            ReservationDiscard.Discarded => ServiceResult<ReservationDiscarded, ReservationFailure>.Success(
                new ReservationDiscarded(id)),
            ReservationDiscard.RecordingCameOfIt =>
                ServiceResult<ReservationDiscarded, ReservationFailure>.Failure(
                    $"A recording came of reservation {id.Value}, and the recording is what is kept. Throw that "
                    + "recording away first, and this reservation goes after it.",
                    ReservationFailure.RecordingCameOfIt),
            ReservationDiscard.TurningIntoARecording =>
                ServiceResult<ReservationDiscarded, ReservationFailure>.Failure(
                    $"Reservation {id.Value} has been taken up and the recording it is turning into is not "
                    + "written down yet, so there is nothing to throw away before it. Asking again once that "
                    + "recording is there says which recording to throw away first.",
                    ReservationFailure.TurningIntoARecording),
            _ => Missing<ReservationDiscarded>(id),
        };
    }

    private ServiceResult<ReservationSettlement, ReservationFailure>? Refuses(
        Reservation reservation,
        ReservationMove move)
    {
        if (reservation.IsPinned)
        {
            return ServiceResult<ReservationSettlement, ReservationFailure>.Failure(
                $"Reservation {reservation.Id.Value} is being recorded right now. It goes on holding the tuner it "
                + "was given until the recording ends, so what to change here is the recording rather than the "
                + "reservation behind it.",
                ReservationFailure.AlreadyRecording);
        }

        if (move is ReservationMove.Restore)
        {
            if (reservation.State is not ReservationState.Cancelled)
            {
                return ServiceResult<ReservationSettlement, ReservationFailure>.Failure(
                    $"Reservation {reservation.Id.Value} stands as {reservation.State}, and restoring brings back "
                    + "one that was cancelled.",
                    ReservationFailure.NotCancelled);
            }

            if (reservation.EffectiveEndAt <= clock.GetUtcNow().UtcDateTime)
            {
                return ServiceResult<ReservationSettlement, ReservationFailure>.Failure(
                    $"The window reservation {reservation.Id.Value} was cancelled out of closed at "
                    + $"{reservation.EffectiveEndAt:O}. Bringing it back would leave a row nothing will ever "
                    + "record.",
                    ReservationFailure.AlreadyOver);
            }

            return null;
        }

        return reservation.State is ReservationState.Scheduled or ReservationState.Conflict
            ? null
            : ServiceResult<ReservationSettlement, ReservationFailure>.Failure(
                $"Reservation {reservation.Id.Value} stands as {reservation.State}, and only one that is still "
                + "waiting for its tuner is changed.",
                ReservationFailure.NotStanding);
    }

    private async Task<ServiceResult<ReservationSettlement, ReservationFailure>> SettledAsync(
        SchedulingRun run,
        Reservation subject,
        CancellationToken cancellationToken)
    {
        if (run.Refusal is SchedulingRefusal.CapacityUnknown)
        {
            return ServiceResult<ReservationSettlement, ReservationFailure>.Failure(
                "The tuners cannot be counted right now, so nothing was decided and nothing was written. A "
                + "reservation is answered as secured or contended or as having nowhere to tune, never as "
                + "something to find out later.",
                ReservationFailure.TunersCannotBeCounted);
        }

        if (!run.Settled)
        {
            return ServiceResult<ReservationSettlement, ReservationFailure>.Failure(
                "Another reservation arrived while this one was being worked out, so nothing was written. Asking "
                + "again reads the newcomer in.",
                ReservationFailure.SomethingArrivedWhileReading);
        }

        if (!run.Plan.Answers(subject.Id))
        {
            return ServiceResult<ReservationSettlement, ReservationFailure>.Success(
                new ReservationSettlement(subject, null, [], run.SeatsLeftOut));
        }

        AllocationDecision decision = run.Plan.For(subject.Id);
        var instead = new List<Reservation>();

        foreach (ReservationId id in decision.Instead)
        {
            if (await reservations.FindAsync(id, cancellationToken) is { } recorded)
            {
                instead.Add(recorded);
            }
        }

        return ServiceResult<ReservationSettlement, ReservationFailure>.Success(
            new ReservationSettlement(subject, decision.Verdict, instead, run.SeatsLeftOut));
    }

    private static ProgrammeSnapshot Snapshot(Programme programme, DateTime at)
        => new(
            Clipped(programme.Name, Reservation.NameMaxLength),
            Clipped(programme.Summary, Reservation.SummaryMaxLength),
            Clipped(Extended(programme.Items), Reservation.ExtendedMaxLength),
            programme.Genres,
            at);

    private static string Extended(IReadOnlyList<ProgrammeItem> items)
        => string.Join("\n\n", items.Select(item => $"{item.Heading}\n{item.Text}"));

    private static string Clipped(string text, int longest)
        => text.Length <= longest ? text : text[..longest];

    private static ServiceResult<T, ReservationFailure> Missing<T>(ReservationId id)
        => ServiceResult<T, ReservationFailure>.Failure(
            $"There is no reservation {id.Value}.",
            ReservationFailure.NoSuchReservation);

    private static ServiceResult<T, ReservationFailure> AlreadyReserved<T>(Reservation already)
        => ServiceResult<T, ReservationFailure>.Failure(
            $"That broadcast is already reserved as {already.Id.Value}, standing as {already.Standing}. A "
            + "cancellation is kept rather than deleted, so a reservation that was cancelled still holds the "
            + "place and is restored rather than made again.",
            ReservationFailure.AlreadyReserved);
}
