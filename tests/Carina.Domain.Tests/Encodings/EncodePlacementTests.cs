using Carina.Domain.Encodings;

namespace Carina.Domain.Tests.Encodings;

public sealed class EncodePlacementTests
{
    [Fact(DisplayName = "BR-ED2-009: nothing at the destination means the work file moves there")]
    public void NothingAtTheDestinationMeansTheWorkFileMovesThere()
        => Assert.Equal(
            EncodePlacementVerdict.Move,
            EncodePlacements.Judge(somethingIsThere: false, thisJobHadAlreadyClaimedTheName: false));

    [Fact(DisplayName = "BR-ED2-009: nothing at the destination after this job had claimed the name still means moving there")]
    public void NothingAtTheDestinationAfterAnEarlierClaimStillMeansMovingThere()
        => Assert.Equal(
            EncodePlacementVerdict.Move,
            EncodePlacements.Judge(somethingIsThere: false, thisJobHadAlreadyClaimedTheName: true));

    [Fact(DisplayName = "BR-ED2-009: a file at a name this job had already written into the ledger is its own success, seen again")]
    public void AFileAtANameThisJobHadAlreadyWrittenIntoTheLedgerIsItsOwnSuccessSeenAgain()
        => Assert.Equal(
            EncodePlacementVerdict.Reconfirm,
            EncodePlacements.Judge(somethingIsThere: true, thisJobHadAlreadyClaimedTheName: true));

    [Fact(DisplayName = "BR-ED2-009: a file at a name this job has only just claimed belongs to nobody the ledger knows, and is not overwritten")]
    public void AFileAtANameThisJobHasOnlyJustClaimedIsACollision()
        => Assert.Equal(
            EncodePlacementVerdict.Collision,
            EncodePlacements.Judge(somethingIsThere: true, thisJobHadAlreadyClaimedTheName: false));

    [Fact(DisplayName = "BR-ED2-009: a collision is a failure with that name, never a renumbering")]
    public void ACollisionIsAFailureWithThatName()
        => Assert.Equal(EncodeFailure.DestinationCollision, EncodePlacements.WhatACollisionIsCalled);
}
