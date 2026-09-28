namespace Carina.Domain.Programmes;

/// <summary>
/// The running status the present/following table last announced for a programme. The schedule
/// tables announce none, so a programme only ever read from them stays undetermined.
/// </summary>
public enum ProgrammeRunning
{
    Undetermined = 0,

    NotRunning = 1,

    StartsInSeconds = 2,

    Pausing = 3,

    Running = 4,

    OffAir = 5,
}
