using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Collection;

namespace Carina.Infrastructure.Recordings;

public static class RecordingStartReading
{
    public static RecordingStartFailure? Of(DriverProblem? problem)
    {
        if (SessionRefusalReading.TuneFailureIn(problem) is { } tuneFailure)
        {
            return RecordingStartFailure.TheTunerWouldNotTune(tuneFailure);
        }

        return problem?.Title is SessionRefusalTitles.NoDeviceFree or SessionRefusalTitles.DeviceBusy
            ? RecordingStartFailure.NoTunerWasFree
            : null;
    }
}
