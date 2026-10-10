namespace Carina.Domain.DataBroadcast;

/// <summary>
/// Where the record of a recording's data broadcast stands: not yet due, coming, made, missing for want of
/// one in the recording, or failed.
/// </summary>
public enum DataBroadcastState
{
    None = 1,

    Coming = 2,

    Made = 3,

    Missing = 4,

    Failed = 5,
}
