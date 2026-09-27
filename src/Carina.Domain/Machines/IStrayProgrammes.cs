namespace Carina.Domain.Machines;

public enum StrayFate
{
    Stopped = 1,

    AlreadyGone = 2,

    AnotherProgrammeHasThatId = 3,

    CouldNotBeStopped = 4,
}

/// <summary>
/// Stops a programme an earlier process started and never got to stop. Only a process under the
/// recorded id that began when the recorded one began is stopped; anything else is left alone.
/// </summary>
public interface IStrayProgrammes
{
    StrayFate Stop(RunningProgramme written);
}
