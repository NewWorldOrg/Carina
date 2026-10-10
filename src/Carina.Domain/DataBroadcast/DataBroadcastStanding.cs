namespace Carina.Domain.DataBroadcast;

/// <summary>
/// Whether the data broadcast of a recording can be played beside it: ready now, still coming, or not at all.
/// </summary>
public enum DataBroadcastStanding
{
    Ready = 1,

    Coming = 2,

    None = 3,
}
