using Carina.Contracts;

namespace Carina.Driver.Descrambling;

/// <summary>
/// What the head of a stream is held for before any of it is handed on: every programme the PAT lists, or no more than
/// it takes for every scrambled packet held to be readable.
/// </summary>
public enum HeadHold
{
    EveryProgramme,

    /// <summary>
    /// The head goes once every scrambled packet held so far can be read, which no programme that has not sent a packet
    /// yet holds back. Every programme the tuner is carrying has sent packets by then, so a session that comes to share
    /// the tuner gets none of its own still scrambled.
    /// </summary>
    WhatIsHeld,
}

public static class HeadHolds
{
    /// <summary>
    /// A live viewer waits for no more than what is held; every other purpose waits for every programme.
    /// </summary>
    public static HeadHold For(SessionPurpose purpose) =>
        purpose is SessionPurpose.Live ? HeadHold.WhatIsHeld : HeadHold.EveryProgramme;

    /// <summary>
    /// Whether the tuner is read from the moment its frontend locks. Only a live viewer is, so a recording keeps
    /// starting from the bytes that follow the adapter's own start of transfer after the lock.
    /// </summary>
    public static bool ReadsFromTheLock(HeadHold hold) => hold is HeadHold.WhatIsHeld;
}
