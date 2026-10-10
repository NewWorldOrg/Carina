namespace Carina.Domain.DataBroadcast;

/// <summary>
/// Whether an event message fires as soon as it is received or at a moment of the programme's own clock.
/// </summary>
public enum EventTiming
{
    Immediate = 1,

    Npt = 2,
}
