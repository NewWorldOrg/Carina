using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Recordings;

public sealed record RecordingFollowed(RecordingId Id, DateTime EndsAt, bool EndUndecided);

/// <summary>
/// Keeps a recording that is already running on the programme it is recording. The guide is read
/// through the port that only reads it, so nothing here parses a section or writes a programme
/// row: what the EPG heard on present/following is already in the row it keeps, and this is a
/// reader of that row.
///
/// The driver is asked first and the ledger is written second, and the ledger is written with the
/// time the driver actually promised rather than the time that was asked for. The driver holds
/// ends to limits of its own, so a window written from the asking rather than the answer would be
/// a promise nothing made. An end the driver has already answered is not put to it again until the
/// guide announces a later one, so an answer that grants nothing is asked for once and not once
/// per tick.
///
/// One recording failing to be followed says nothing about the next, so each is followed inside
/// its own guard: an unreadable guide row or a driver that throws leaves that recording on the
/// window it already holds and the rest of the round untouched.
/// </summary>
public sealed class ProgramExtensionFollower(
    IRecordingRepository recordings,
    IAnnouncedProgrammes programmes,
    IDriverClient driver,
    EndsAlreadyAsked asked,
    RecordingSettings settings,
    ILogger<ProgramExtensionFollower> logger)
{
    public async Task<IReadOnlyList<RecordingFollowed>> FollowAsync(
        IReadOnlyList<Recording> running,
        IReadOnlyList<RecordingTick> reservations,
        DateTime now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(running);
        ArgumentNullException.ThrowIfNull(reservations);

        asked.KeepOnly(running.Select(recording => recording.Id).ToHashSet());

        List<RecordingFollowed> followed = [];

        foreach (Recording recording in running)
        {
            if (recording.AbortedAt is not null)
            {
                continue;
            }

            try
            {
                if (await FollowedAsync(recording, reservations, now, cancellationToken) is { } taken)
                {
                    followed.Add(taken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception failure)
            {
                logger.LogError(
                    failure,
                    "Recording {Recording} could not be followed on this tick; it stays on the window it holds "
                    + "and the rest of the round is unaffected.",
                    recording.Id.Wire);
            }
        }

        return followed;
    }

    private async Task<RecordingFollowed?> FollowedAsync(
        Recording recording,
        IReadOnlyList<RecordingTick> reservations,
        DateTime now,
        CancellationToken cancellationToken)
        => await MovedAsync(recording, reservations, now, cancellationToken) is { } move
            ? await ExtendedAsync(recording, move, now, cancellationToken)
            : null;

    private async Task<WindowMove?> MovedAsync(
        Recording recording,
        IReadOnlyList<RecordingTick> reservations,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (MarginOf(recording, reservations) is not { } marginAfter)
        {
            return null;
        }

        DateTime? heardWholeAt = await programmes.HeardWholeAtAsync(
            recording.NetworkId.Value,
            recording.ServiceId.Value,
            cancellationToken);
        Programme? announced = await programmes.FindAsync(recording.Programme.Id, cancellationToken);

        return ProgrammeFollowing.Next(
            GuideReading.Of(announced, heardWholeAt),
            announced?.EndsAt,
            marginAfter,
            recording.ExpectedWindowEnd,
            now,
            settings.UndecidedEndAhead);
    }

    private async Task<RecordingFollowed?> ExtendedAsync(
        Recording recording,
        WindowMove move,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (asked.AlreadyPut(recording.Id, move.EndsAt))
        {
            return null;
        }

        DriverCall<SessionSnapshot> answer = await driver.ExtendSessionAsync(
            RecordingSessions.Named(recording.Id),
            new DateTimeOffset(move.EndsAt, TimeSpan.Zero),
            cancellationToken);

        if (!answer.TryGetValue(out SessionSnapshot? session))
        {
            if (answer.Outcome is not DriverCallOutcome.Unreachable)
            {
                asked.Answered(recording.Id, move.EndsAt);
            }

            logger.LogWarning(
                "Recording {Recording} runs past {Held:O} and the driver would not hold its tuner that long "
                + "({Outcome}); the window it was promised stands and is what stops it.",
                recording.Id.Wire,
                recording.ExpectedWindowEnd,
                answer.Outcome);

            return null;
        }

        DateTime granted = session.EndsAt is { } promised
            ? promised.UtcDateTime
            : move.EndsAt;

        if (granted <= recording.ExpectedWindowEnd)
        {
            asked.Answered(recording.Id, move.EndsAt);

            logger.LogInformation(
                "Recording {Recording} asked to run until {Asked:O} and the driver promised {Granted:O}, which is "
                + "no later than the window it already holds.",
                recording.Id.Wire,
                move.EndsAt,
                granted);

            return null;
        }

        recording.Extend(granted);

        if (move.EndUndecided && !SaysTheEndWasUndecided(recording))
        {
            recording.Note(new OutcomeDetail(RecordingFault.EndStillUndecided, null, string.Empty, now));
        }

        await recordings.SaveAsync(recording, cancellationToken);

        asked.Answered(recording.Id, move.EndsAt);

        logger.LogInformation(
            "Recording {Recording} follows its programme to {Granted:O}{Undecided}.",
            recording.Id.Wire,
            granted,
            move.EndUndecided ? ", which announces no end of its own" : string.Empty);

        return new RecordingFollowed(recording.Id, granted, move.EndUndecided);
    }

    private static bool SaysTheEndWasUndecided(Recording recording)
        => recording.OutcomeDetail.Any(detail => detail.Fault is RecordingFault.EndStillUndecided);

    private static TimeSpan? MarginOf(Recording recording, IReadOnlyList<RecordingTick> reservations)
    {
        if (recording.ReservationId is not { } reservationId)
        {
            return null;
        }

        foreach (RecordingTick tick in reservations)
        {
            if (tick.Id.Equals(reservationId))
            {
                return tick.MarginAfter;
            }
        }

        return null;
    }
}
