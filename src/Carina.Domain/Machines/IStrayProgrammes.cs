namespace Carina.Domain.Machines;

public enum StrayFate
{
    Stopped = 1,

    AlreadyGone = 2,

    AnotherProgrammeHasThatId = 3,

    CouldNotBeStopped = 4,
}

/// <summary>
/// Stops a programme an earlier process started and never got to stop. The programme is the one
/// written down only if what runs under its id now began when it began; anything else under
/// that id is somebody else's and is left alone.
/// </summary>
public interface IStrayProgrammes
{
    StrayFate Stop(RunningProgramme written);
}
