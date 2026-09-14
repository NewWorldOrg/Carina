using Carina.Contracts;
using Carina.Domain.Recordings;

namespace Carina.Infrastructure.Recordings;

/// <summary>
/// The few moves that put a recording back on a stream, shared by the pass that watches a running
/// recording and by the recovery that finds one nobody was watching. A resumed recording is asked
/// for under its own session name and its own output root, so the driver opens the file it already
/// has and writes on the end of it rather than starting a second one.
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
