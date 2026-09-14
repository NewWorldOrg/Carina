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
/// time the driver actually promised rather than the time that was asked for. A recording riding
/// along on somebody else's tuner is cut back to the host's own end, and a window written from the
/// asking rather than the answer would be a promise nothing can keep.
/// </summary>
public sealed class ProgramExtensionFollower(
    IRecordingRepository recordings,
    IAnnouncedProgrammes programmes,
    IDriverClient driver,
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

        List<RecordingFollowed> followed = [];

        foreach (Recording recording in running)
        {
            if (recording.AbortedAt is not null)
            {
                continue;
            }

            if (await MovedAsync(recording, reservations, now, cancellationToken) is not { } move)
            {
                continue;
            }

            if (await ExtendedAsync(recording, move, now, cancellationToken) is { } taken)
            {
                followed.Add(taken);
            }
        }

        return followed;
    }

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
        DriverCall<SessionSnapshot> answer = await driver.ExtendSessionAsync(
            RecordingSessions.Named(recording.Id),
            new DateTimeOffset(move.EndsAt, TimeSpan.Zero),
            cancellationToken);

        if (!answer.TryGetValue(out SessionSnapshot? session))
        {
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
