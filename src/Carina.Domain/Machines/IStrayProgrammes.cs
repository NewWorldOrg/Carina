namespace Carina.Domain.Machines;

public enum StrayFate
{
    Stopped = 1,

    AlreadyGone = 2,

    AnotherProgrammeHasThatId = 3,

    CouldNotBeStopped = 4,
}

public interface IStrayProgrammes
{
    StrayFate Stop(RunningProgramme written);
}
