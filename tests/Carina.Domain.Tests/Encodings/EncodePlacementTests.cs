using Carina.Domain.Encodings;

namespace Carina.Domain.Tests.Encodings;

public sealed class EncodePlacementTests
{
    [Fact(DisplayName = "nothing at the destination means the work file moves there")]
    public void NothingAtTheDestinationMeansTheWorkFileMovesThere()
        => Assert.Equal(
            EncodePlacementVerdict.Move,
            EncodePlacements.Judge(
                somethingIsThere: false,
                thisJobHadAlreadyClaimedTheName: false,
                thisJobBroughtAReplacement: false));

    [Fact(DisplayName = "nothing at the destination after this job had claimed the name still means moving there")]
    public void NothingAtTheDestinationAfterAnEarlierClaimStillMeansMovingThere()
        => Assert.Equal(
            EncodePlacementVerdict.Move,
            EncodePlacements.Judge(
                somethingIsThere: false,
                thisJobHadAlreadyClaimedTheName: true,
                thisJobBroughtAReplacement: false));

    [Fact(DisplayName = "a file at a name this job had already written into the ledger is its own success, seen again")]
    public void AFileAtANameThisJobHadAlreadyWrittenIntoTheLedgerIsItsOwnSuccessSeenAgain()
        => Assert.Equal(
            EncodePlacementVerdict.Reconfirm,
            EncodePlacements.Judge(
                somethingIsThere: true,
                thisJobHadAlreadyClaimedTheName: true,
                thisJobBroughtAReplacement: false));

    [Fact(DisplayName = "a file at a name this job has only just claimed belongs to nobody the ledger knows, and is not overwritten")]
    public void AFileAtANameThisJobHasOnlyJustClaimedIsACollision()
        => Assert.Equal(
            EncodePlacementVerdict.Collision,
            EncodePlacements.Judge(
                somethingIsThere: true,
                thisJobHadAlreadyClaimedTheName: false,
                thisJobBroughtAReplacement: false));

    [Fact(DisplayName = "a job that brought a replacement puts it where the artefact stands, rather than calling it a collision")]
    public void AJobThatBroughtAReplacementPutsItWhereTheArtefactStands()
        => Assert.Equal(
            EncodePlacementVerdict.Replace,
            EncodePlacements.Judge(
                somethingIsThere: true,
                thisJobHadAlreadyClaimedTheName: false,
                thisJobBroughtAReplacement: true));

    [Fact(DisplayName = "a job that brought a replacement puts it there even where the name was already its own")]
    public void AJobThatBroughtAReplacementPutsItThereEvenWhereTheNameWasAlreadyItsOwn()
        => Assert.Equal(
            EncodePlacementVerdict.Replace,
            EncodePlacements.Judge(
                somethingIsThere: true,
                thisJobHadAlreadyClaimedTheName: true,
                thisJobBroughtAReplacement: true));

    [Fact(DisplayName = "a replacement with nothing at the destination is only a move, because there is nothing to replace")]
    public void AReplacementWithNothingAtTheDestinationIsOnlyAMove()
        => Assert.Equal(
            EncodePlacementVerdict.Move,
            EncodePlacements.Judge(
                somethingIsThere: false,
                thisJobHadAlreadyClaimedTheName: false,
                thisJobBroughtAReplacement: true));

    [Fact(DisplayName = "a collision is a failure with that name, never a renumbering")]
    public void ACollisionIsAFailureWithThatName()
        => Assert.Equal(EncodeFailure.DestinationCollision, EncodePlacements.WhatACollisionIsCalled);
}
