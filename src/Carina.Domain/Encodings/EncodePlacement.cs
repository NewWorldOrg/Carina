namespace Carina.Domain.Encodings;

public enum EncodePlacementVerdict
{
    Move = 1,

    Reconfirm = 2,

    Collision = 3,

    Replace = 4,
}

/// <summary>
/// What to do with a finished work file once its name is in the ledger. A file already at that name
/// is either this job's own earlier success, or a collision that is never overwritten. The one file
/// written over is the one a job asked to make the artefact again replaces.
/// </summary>
public static class EncodePlacements
{
    public const EncodeFailure WhatACollisionIsCalled = EncodeFailure.DestinationCollision;

    public static EncodePlacementVerdict Judge(
        bool somethingIsThere,
        bool thisJobHadAlreadyClaimedTheName,
        bool thisJobBroughtAReplacement)
    {
        if (!somethingIsThere)
        {
            return EncodePlacementVerdict.Move;
        }

        return thisJobBroughtAReplacement ? EncodePlacementVerdict.Replace
            : thisJobHadAlreadyClaimedTheName ? EncodePlacementVerdict.Reconfirm
            : EncodePlacementVerdict.Collision;
    }
}
