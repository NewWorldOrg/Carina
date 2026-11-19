namespace Carina.Domain.Encodings;

public enum EncodePlacementVerdict
{
    Move = 1,

    Reconfirm = 2,

    Collision = 3,

    Replace = 4,
}

public static class EncodePlacements
{
    public const EncodeFailure WhatACollisionIsCalled = EncodeFailure.DestinationCollision;

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
