using Carina.Api.Common;
using Carina.Domain.Auth;
using Carina.Domain.Recordings;

namespace Carina.Api.Services;

public enum PlaybackTicketRefusal
{
    NoSuchRecording = 1,

    StillBeingWritten = 2,

    TooManyOutstanding = 3,
}

public sealed class PlaybackTicketService(IRecordingDirectory recordings, IPlaybackTicketStore tickets)
{
    public static PlaybackTarget TargetOf(RecordingId id)
    {
        ArgumentNullException.ThrowIfNull(id);

        return PlaybackTarget.Recording(id.Wire);
    }

    public async Task<ServiceResult<IssuedPlaybackTicket, PlaybackTicketRefusal>> IssueAsync(
        RecordingId id,
        Subject watcher,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(watcher);

        if (await recordings.FindAsync(id, cancellationToken) is not { } recording)
        {
            return Refused(PlaybackTicketRefusal.NoSuchRecording, $"There is no recording {id.Wire}.");
        }

        if (recording.IsInFlight)
        {
            return Refused(
                PlaybackTicketRefusal.StillBeingWritten,
                $"Recording {id.Wire} is still being written, so there is no whole file for a player to open.");
        }

        return tickets.Issue(watcher, TargetOf(id)) is { } issued
            ? ServiceResult<IssuedPlaybackTicket, PlaybackTicketRefusal>.Success(issued)
            : Refused(
                PlaybackTicketRefusal.TooManyOutstanding,
                "Too many playback tickets are outstanding to issue another one now.");
    }

    private static ServiceResult<IssuedPlaybackTicket, PlaybackTicketRefusal> Refused(
        PlaybackTicketRefusal refusal,
        string said)
        => ServiceResult<IssuedPlaybackTicket, PlaybackTicketRefusal>.Failure(said, refusal);
}
