using Carina.Domain.Recordings;

using Puppis.Contracts;

namespace Carina.Infrastructure.Recordings;

internal static class RecordingResumption
{
    public static bool OpenABreak(Recording recording, RecordingFault fault, DateTime at)
    {
        if (recording.IsInterrupted)
        {
            return false;
        }

        recording.Interrupt(fault, at);

        return true;
    }

    public static bool CloseAnyOpenBreak(Recording recording, DateTime at)
    {
        if (!recording.IsInterrupted)
        {
            return false;
        }

        recording.Resume(at);

        return true;
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
