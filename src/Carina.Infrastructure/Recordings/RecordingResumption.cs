using Carina.Contracts;
using Carina.Domain.Recordings;

namespace Carina.Infrastructure.Recordings;

/// <summary>
/// The moves that put a recording back on a stream, shared by the stream watcher and by recovery.
/// A resumed recording is asked for under its own session name and output root, so the driver
/// appends to the file it already has.
/// </summary>
internal static class RecordingResumption
{
    public static bool OpenABreak(Recording recording, RecordingFault fault, DateTime at)
    {
        if (recording.Interruptions.Count > 0 && recording.Interruptions[^1].IsOpen)
        {
            return false;
        }

        recording.Interrupt(fault, at);

        return true;
    }

    /// <summary>
    /// When a recording stopped being written: the last write to its file, kept between the moment it was last
    /// written by an earlier stretch and the moment the break was noticed, or the moment it was noticed when the file
    /// says nothing.
    /// </summary>
    public static DateTime StoppedWritingAt(Recording recording, DateTime? lastWritten, DateTime noticed)
    {
        if (lastWritten is not { } written || written >= noticed)
        {
            return noticed;
        }

        DateTime earliest = recording.Interruptions.Count is 0
            ? recording.StartedAtActual
            : recording.Interruptions[^1].ResumedAt ?? recording.Interruptions[^1].OccurredAt;

        return written > earliest ? written : earliest;
    }

    public static bool CloseAnyOpenBreak(Recording recording, DateTime at)
    {
        if (recording.Interruptions.Count is 0 || !recording.Interruptions[^1].IsOpen)
        {
            return false;
        }

        recording.Resume(at);

        return true;
    }

    public static void Adopt(Recording recording, string deviceId)
    {
        if (recording.TunerDeviceId is null && deviceId is { Length: > 0 })
        {
            recording.Acquire(new TunerDeviceId(deviceId));
        }
    }

    public static StartSessionRequest Request(Recording recording, TuneParams tune)
        => new()
        {
            SessionId = RecordingSessions.Named(recording.Id),
            Purpose = SessionPurpose.Recording,
            Tuning = tune.ToLegacyRequest(),
            Tune = tune,
            OutputRoot = recording.OutputRoot.Value,
            RecordingId = recording.Id.Wire,
            EndsAt = recording.ExpectedWindowEnd,
        };
}
