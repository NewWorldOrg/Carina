namespace Carina.Domain.Encodings;

public enum EncodePlacementVerdict
{
    Move = 1,

    Reconfirm = 2,

    Collision = 3,
}

/// <summary>
/// What to do with a finished work file once its name is in the ledger. The ledger is written
/// first, so a file already at that name is either this job's own earlier success — the name was
/// this job's before this attempt began — or something the ledger never heard of, which is a
/// collision and is never overwritten (BR-ED2-009).
/// </summary>
public static class EncodePlacements
{
    public const EncodeFailure WhatACollisionIsCalled = EncodeFailure.DestinationCollision;

    public static EncodePlacementVerdict Judge(bool somethingIsThere, bool thisJobHadAlreadyClaimedTheName)
        => !somethingIsThere ? EncodePlacementVerdict.Move
            : thisJobHadAlreadyClaimedTheName ? EncodePlacementVerdict.Reconfirm
            : EncodePlacementVerdict.Collision;
}
