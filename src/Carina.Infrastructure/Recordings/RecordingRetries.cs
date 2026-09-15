using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Recordings;

/// <summary>
/// The ledger's half of trying a failed start again. It reads what the ledger holds about one
/// reservation's starts, sets that beside the guide, the disk and the channel's rotation, asks the
/// decision, and writes down every attempt and the giving up. It starts nothing: an attempt is the
/// tick's ordinary start, through the same claim, the same precheck and the same request to the
/// driver as the first one, so it can only have a tuner the first one could have had.
/// </summary>
public sealed class RecordingRetries(
    IReservationRepository reservations,
    IReservationOutcomeRepository outcomes,
    ICandidateChannelRepository candidates,
    IAnnouncedProgrammes programmes,
    DiskPrecheckService disks,
    RecordingSettings settings,
    RetryPolicy policy,
    ILogger<RecordingRetries> logger)
{
    public async Task<RetryHistory?> HistoryAsync(ReservationId reservation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        return StartRetry.HistoryOf(await outcomes.ListForReservationAsync(reservation, cancellationToken));
    }

    public async Task<RetryVerdict> WeighAsync(
        RecordingTick due,
        RetryHistory history,
        TuningResolution resolution,
        IReadOnlyList<RecordingDemand> running,
        DateTime now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(due);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(running);

        TuningParameters tuning = resolution.Tuning
            ?? throw new ArgumentException("A start is weighed for another attempt only on a channel it can tune.", nameof(resolution));
        CandidateChannel? candidate = resolution.CandidateChannelId is { } tunedWith
            ? await candidates.FindAsync(tunedWith, cancellationToken)
            : null;

        RetryVerdict verdict = StartRetry.For(
            new RetrySighting(
                history.FailureClasses,
                OrphanRecovery.StillOnAir(
                    await GuideSaysAsync(due, cancellationToken),
                    now < due.EffectiveEndAt - due.MarginAfter),
                history.AttemptsSoFar,
                history.LastAttemptAt,
                candidate is { IsInRotation: false },
                candidate?.NextAttemptAt,
                await FoundNoRoomAsync(due, tuning, running, now, cancellationToken)),
            policy,
            now);

        if (verdict.Reason is { } reason)
        {
            await GiveUpAsync(
                due.Id,
                reason,
                reason is RetryGiveUp.NotTransient ? StartRetry.StructuralAmong(history.FailureClasses) : null,
                now,
                cancellationToken);
        }

        return verdict;
    }

    public async Task TriedAsync(
        ReservationId reservation,
        RetryAttempt attempt,
        DateTime at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentNullException.ThrowIfNull(attempt);

        if (await reservations.FindAsync(reservation, cancellationToken) is not { } found)
        {
            logger.LogWarning(
                "A start was tried again ({Result}) for a reservation that is gone, so the ledger has nowhere to "
                + "write it down.",
                attempt.Result);

            return;
        }

        await outcomes.AddAsync(
            ReservationOutcome.RecordRetry(ReservationOutcomeId.New(), found, attempt, at),
            cancellationToken);
    }

    private async Task GiveUpAsync(
        ReservationId reservation,
        RetryGiveUp because,
        TuneFailureKind? structural,
        DateTime at,
        CancellationToken cancellationToken)
    {
        if (await reservations.FindAsync(reservation, cancellationToken) is not { } found)
        {
            logger.LogWarning(
                "Trying a start again was given up ({Reason}) for a reservation that is gone, so the ledger has "
                + "nowhere to write it down.",
                because);

            return;
        }

        await outcomes.AddAsync(
            ReservationOutcome.RecordGivingUp(ReservationOutcomeId.New(), found, because, structural, at),
            cancellationToken);

        logger.LogWarning(
            "A reservation whose start failed is not started again: {Reason} ({Structural}).",
            because,
            structural);
    }

    /// <summary>
    /// A guide that cannot be read says nothing about the programme, which is what a guide that has
    /// never heard of it says too, and the window the reservation promised is then what is left to go on.
    /// </summary>
    private async Task<GuideStanding> GuideSaysAsync(RecordingTick due, CancellationToken cancellationToken)
    {
        try
        {
            return GuideReading.Of(
                await programmes.FindAsync(due.Programme.Id, cancellationToken),
                await programmes.HeardWholeAtAsync(due.NetworkId.Value, due.ServiceId.Value, cancellationToken));
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            logger.LogWarning(
                failure,
                "The guide could not be read while a failed start was weighed for another attempt, so only the "
                + "window the reservation promised says whether its programme is still on the air.");

            return GuideStanding.NothingKnown;
        }
    }

    /// <summary>
    /// A disk that cannot be weighed is not a disk found to have no room. The attempt goes ahead, weighs
    /// it again the way every start does, and a start that cannot weigh it is abandoned and written down
    /// as an attempt nothing answered.
    /// </summary>
    private async Task<bool> FoundNoRoomAsync(
        RecordingTick due,
        TuningParameters tuning,
        IReadOnlyList<RecordingDemand> running,
        DateTime now,
        CancellationToken cancellationToken)
    {
        RecordingWindow window = RecordingWindow.Promised(due.EffectiveStartAt, due.EffectiveEndAt, settings.TuningLead);

        try
        {
            DiskPrecheckVerdict verdict = await disks.WeighAsync(
                settings.OutputRoot,
                new RecordingDemand(tuning.Typed().Kind, window.Start, window.End),
                running,
                now,
                cancellationToken);

            return !verdict.HasRoom;
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            logger.LogWarning(
                failure,
                "The disk could not be weighed while a failed start was weighed for another attempt; the attempt "
                + "weighs it again as every start does.");

            return false;
        }
    }
}
