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

    /// <summary>
    /// Whether a file of <paramref name="bytes"/> already at this job's own name, left by an earlier attempt, is kept
    /// as the artefact. An empty one is not what an attempt places, and is a collision.
    /// </summary>
    public static bool KeepsAnEarlierAttemptOf(long bytes) => bytes > 0;

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
