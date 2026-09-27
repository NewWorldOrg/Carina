namespace Carina.Architecture.Tests;

public sealed class RecordingFenceRuleTests
{
    [Fact(DisplayName = "nothing outside recording and the carriage writes the claim or the outcome sideways")]
    public void NothingOutsideRecordingAndTheCarriageWritesTheClaimOrTheOutcomeSideways()
    {
        Assert.Empty(RecordingFenceRules.WritersOfWhatRecordingOwnsThroughThePropertyBag(
            RepositoryLayout.SourceDirectory));
    }

    [Fact(DisplayName = "the two allowed to write them are recording and the carriage that rehydrates old rows")]
    public void TheTwoAllowedToWriteThemAreRecordingAndTheCarriageThatRehydratesOldRows()
    {
        Assert.Equal(
            [
                "/Recordings/",
                "/Migration/",
                "/Carina.Infrastructure/Persistence/Repositories/ReservationRecordingContract.cs",
            ],
            ReservationRules.AllowedToWriteThem);

        Assert.Equal(["started_at", "recording_outcome"], ReservationRules.RecordingOwnedColumns);
    }

    [Fact(DisplayName = "the recording feature reads no broadcast table of its own")]
    public void TheRecordingFeatureReadsNoBroadcastTableOfItsOwn()
    {
        Assert.Empty(RecordingFenceRules.BroadcastTableReadersInsideTheRecordingFeature(
            RepositoryLayout.SourceDirectory));
    }

    [Fact(DisplayName = "the port the recording round holds carries no way to write the guide")]
    public void ThePortTheRecordingRoundHoldsCarriesNoWayToWriteTheGuide()
    {
        Assert.Empty(RecordingFenceRules.WriteMembersOnThePortTheRoundHolds(RepositoryLayout.SourceDirectory));
    }

    [Fact(DisplayName = "those write members are still on the port collection holds, for the rule to have missed them")]
    public void ThoseWriteMembersAreStillOnThePortCollectionHolds()
    {
        Assert.Equal(
            ["AbsorbAsync", "AddAsync", "ForgetAsync", "ForgetEverythingAsync"],
            RecordingFenceRules.WriteMembersOnThePortCollectionHolds(RepositoryLayout.SourceDirectory));
    }

    [Fact(DisplayName = "every place in recording that reaches the guide holds the read-only port")]
    public void EveryPlaceInRecordingThatReachesTheGuideHoldsTheReadOnlyPort()
    {
        Assert.Equal(
            [
                "/Carina.Infrastructure/Recordings/OrphanRecoveryService.cs",
                "/Carina.Infrastructure/Recordings/ProgramExtensionFollower.cs",
                "/Carina.Infrastructure/Recordings/RecordingRetries.cs",
                "/Carina.Infrastructure/Recordings/RecordingRound.cs",
            ],
            RecordingFenceRules.HoldersOfTheReadOnlyGuidePort(RepositoryLayout.SourceDirectory));
    }

    [Fact(DisplayName = "and nothing in the recording feature names the port that can write it")]
    public void NothingInTheRecordingFeatureNamesThePortThatCanWriteTheGuide()
        => Assert.Empty(RecordingFenceRules.NamersOfTheWritingGuidePort(RepositoryLayout.SourceDirectory));

    [Fact(DisplayName = "the recording feature offers no deletion beside the one route the library owns")]
    public void TheRecordingFeatureOffersNoDeletionBesideTheOneRouteTheLibraryOwns()
    {
        Assert.Empty(RecordingFenceRules.DeletionsOfferedByTheRecordingFeature(RepositoryLayout.SourceDirectory));
    }

    [Fact(DisplayName = "one route throws a recording away, and it is the one the canon was corrected to")]
    public void OneRouteThrowsARecordingAway()
    {
        Assert.Equal(
            ["api/recordings/{id}"],
            RecordingFenceRules.RoutesThatThrowARecordingAway(RepositoryLayout.SourceDirectory));
    }

    [Fact(DisplayName = "that route cannot reach the ledger without the mount checks first")]
    public void ThatRouteCannotReachTheLedgerWithoutTheMountChecksFirst()
    {
        string action = File.ReadAllText(Path.Combine(
            RepositoryLayout.SourceDirectory,
            "Carina.Api",
            "Controllers",
            "Recordings",
            "DeleteRecordingAction.cs"));

        string service = File.ReadAllText(Path.Combine(
            RepositoryLayout.SourceDirectory,
            "Carina.Api",
            "Services",
            "RecordingService.cs"));

        Assert.Contains("recordings.DiscardAsync(recordingId", action, StringComparison.Ordinal);
        Assert.Contains("IRecordingFileEraser eraser", service, StringComparison.Ordinal);
        Assert.True(
            service.IndexOf("eraser.EraseAsync(", StringComparison.Ordinal)
            < service.IndexOf("recordings.DiscardAsync(id", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "one place can erase a ledger row, and it is the one the guarded route reaches")]
    public void OnePlaceCanEraseALedgerRowAndItIsTheOneTheGuardedRouteReaches()
    {
        Assert.Equal(
            [RecordingFenceRules.ErasureTheGuardedRouteReaches],
            RecordingFenceRules.WhatErasesARecordingLedgerRow(RepositoryLayout.SourceDirectory));
    }
}
