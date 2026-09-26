namespace Carina.Domain.Encodings;

public enum EncodePlacementVerdict
{
    Move = 1,

    Reconfirm = 2,

    Collision = 3,

    Replace = 4,
}

/// <summary>
/// What to do with a finished work file once its name is in the ledger. The ledger is written
/// first, so a file already at that name is either this job's own earlier success — the name was
/// this job's before this attempt began — or something the ledger never heard of, which is a
/// collision and is never overwritten.
/// <para>
/// The one file that is written over is the one a person asked to have made again: such a job
/// brings a replacement, and putting it there is the whole point of the asking. A job that brought
/// none is judged exactly as it was before, so nothing is overwritten unless somebody asked.
/// </para>
/// </summary>
public static class EncodePlacements
{
    public const EncodeFailure WhatACollisionIsCalled = EncodeFailure.DestinationCollision;

    public static EncodePlacementVerdict Judge(
        bool somethingIsThere,
        bool thisJobHadAlreadyClaimedTheName,
        bool thisJobBroughtAReplacement)
        => !somethingIsThere ? EncodePlacementVerdict.Move
            : thisJobBroughtAReplacement ? EncodePlacementVerdict.Replace
            : thisJobHadAlreadyClaimedTheName ? EncodePlacementVerdict.Reconfirm
            : EncodePlacementVerdict.Collision;
}
