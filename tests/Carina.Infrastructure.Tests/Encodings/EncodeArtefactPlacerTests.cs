using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Encodings;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class EncodeArtefactPlacerTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-ED2-009: the name is in the ledger before anything is at the destination, and the work file is the artefact afterwards")]
    public async Task TheNameIsInTheLedgerBeforeAnythingIsAtTheDestination()
    {
        using var harness = new EncodeHarness();
        EncodeJob job = harness.Running();
        string work = harness.WorkFileOf(job, "the picture");
        string artefact = harness.ArtefactPathOf(job);
        bool nothingThereWhenClaimed = false;
        harness.Jobs.WhenClaiming = (_, _) => nothingThereWhenClaimed = !File.Exists(artefact);

        EncodePlacementOutcome outcome = await harness.Placer.PlaceAsync(job, Cancel);

        Assert.Equal(EncodePlacementOutcome.Moved, outcome);
        Assert.True(nothingThereWhenClaimed, "the ledger was written while the destination was still empty");
        Assert.Equal("the picture", File.ReadAllText(artefact));
        Assert.False(File.Exists(work));
        Assert.Equal(EncodeJobStatus.Completed, job.Status);
        Assert.Equal(EncodeFileName.Artefact(job.RecordingId, job.ProfileId), job.ArtefactName);
        Assert.Equal(harness.Clock.GetUtcNow().UtcDateTime, job.EndedAt);
        Assert.Equal(
            [$"claimed {job.Id.Wire} {job.ArtefactName!.Value}", $"saved {job.Id.Wire} Completed"],
            harness.Jobs.Moves);
    }

    [Fact(DisplayName = "BR-ED2-010: the work file that became the artefact is settled as such in the ledger, not left owed")]
    public async Task TheWorkFileThatBecameTheArtefactIsSettledAsSuch()
    {
        using var harness = new EncodeHarness();
        EncodeJob job = harness.Running();
        harness.WorkFileOf(job, "the picture");

        await harness.Placer.PlaceAsync(job, Cancel);

        EncodeScratchFile recorded = Assert.Single(harness.Scratch.Files);
        Assert.Equal(EncodeScratchFate.BecameTheArtefact, recorded.Fate);
        Assert.False(recorded.IsOwedARemoval);
        Assert.Contains($"settled {job.WorkFileName.Value} BecameTheArtefact", harness.Scratch.Moves);
    }

    [Fact(DisplayName = "BR-ED2-009: a work file written in a working directory of its own is moved from there")]
    public async Task AWorkFileWrittenInAWorkingDirectoryOfItsOwnIsMovedFromThere()
    {
        using var harness = new EncodeHarness(workingBeside: false);
        EncodeJob job = harness.Running();
        string work = harness.WorkFileOf(job, "the picture");

        Assert.StartsWith(harness.Workshop!.Root, work, StringComparison.Ordinal);

        EncodePlacementOutcome outcome = await harness.Placer.PlaceAsync(job, Cancel);

        Assert.Equal(EncodePlacementOutcome.Moved, outcome);
        Assert.Equal("the picture", File.ReadAllText(harness.ArtefactPathOf(job)));
        Assert.False(File.Exists(work));
        Assert.Empty(harness.Workshop.Snapshot());
    }

    [Fact(DisplayName = "BR-ED2-009: a file already at the name this job wrote into the ledger before this attempt is its own success, kept and not overwritten")]
    public async Task AFileAtTheNameThisJobWroteBeforeThisAttemptIsItsOwnSuccess()
    {
        using var harness = new EncodeHarness();
        var recording = RecordingId.New();
        var profile = EncodeProfileId.New();
        EncodeJob job = harness.RunningAgainWithItsName(recording, profile);
        string work = harness.WorkFileOf(job, "the second picture");
        string artefact = harness.ArtefactPathOf(job);
        File.WriteAllText(artefact, "the first picture");

        EncodePlacementOutcome outcome = await harness.Placer.PlaceAsync(job, Cancel);

        Assert.Equal(EncodePlacementOutcome.Reconfirmed, outcome);
        Assert.Equal("the first picture", File.ReadAllText(artefact));
        Assert.True(File.Exists(work), "the fresh work file is left for the ledger sweep to remove");
        Assert.Equal(EncodeJobStatus.Completed, job.Status);
        Assert.True(Assert.Single(harness.Scratch.Files).IsOwedARemoval);
    }

    [Fact(DisplayName = "BR-ED2-009: a file already at a name this job has only just claimed belongs to nobody the ledger knows: a collision, and nothing is overwritten")]
    public async Task AFileAtANameThisJobHasOnlyJustClaimedIsACollision()
    {
        using var harness = new EncodeHarness();
        EncodeJob job = harness.Running();
        string work = harness.WorkFileOf(job, "the picture");
        string artefact = harness.ArtefactPathOf(job);
        File.WriteAllText(artefact, "somebody else's picture");

        EncodePlacementOutcome outcome = await harness.Placer.PlaceAsync(job, Cancel);

        Assert.Equal(EncodePlacementOutcome.Collided, outcome);
        Assert.Equal("somebody else's picture", File.ReadAllText(artefact));
        Assert.Equal("the picture", File.ReadAllText(work));
        Assert.Equal(EncodeJobStatus.Failed, job.Status);
        Assert.Equal(EncodeFailure.DestinationCollision, job.Failure!.Failure);
        Assert.DoesNotContain(harness.Room.Root, job.Failure.Note, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-ED2-009: a name another job holds in the ledger is a collision before the disk is even looked at")]
    public async Task ANameAnotherJobHoldsInTheLedgerIsACollisionBeforeTheDiskIsLookedAt()
    {
        using var harness = new EncodeHarness();
        var recording = RecordingId.New();
        var profile = EncodeProfileId.New();
        EncodeJob first = harness.Running(recording, profile);
        EncodeJob second = harness.Running(recording, profile);
        string firstWork = harness.WorkFileOf(first, "first");
        string secondWork = harness.WorkFileOf(second, "second");

        Assert.NotEqual(firstWork, secondWork);
        Assert.Equal(EncodePlacementOutcome.Moved, await harness.Placer.PlaceAsync(first, Cancel));

        EncodePlacementOutcome outcome = await harness.Placer.PlaceAsync(second, Cancel);

        Assert.Equal(EncodePlacementOutcome.Collided, outcome);
        Assert.Equal("first", File.ReadAllText(harness.ArtefactPathOf(first)));
        Assert.Equal("second", File.ReadAllText(secondWork));
        Assert.Equal(EncodeJobStatus.Failed, second.Status);
        Assert.Equal(EncodeFailure.DestinationCollision, second.Failure!.Failure);
        Assert.Null(second.ArtefactName);
    }

    [Fact(DisplayName = "BR-ED2-009: a move that would cross a mount is refused, and the work file stays where it is")]
    public async Task AMoveThatWouldCrossAMountIsRefused()
    {
        using var harness = new EncodeHarness(workingBeside: false);
        harness.Probe = new ScriptedProbe(new RenameVerdict(RenameStanding.WouldCrossAMount, "Invalid cross-device link"));
        EncodeJob job = harness.Running();
        string work = harness.WorkFileOf(job, "the picture");

        EncodePlacementOutcome outcome = await harness.Placer.PlaceAsync(job, Cancel);

        Assert.Equal(EncodePlacementOutcome.Refused, outcome);
        Assert.True(File.Exists(work));
        Assert.False(File.Exists(harness.ArtefactPathOf(job)));
        Assert.Equal(EncodeJobStatus.Failed, job.Status);
        Assert.Equal(EncodeFailure.CapabilityUnavailable, job.Failure!.Failure);
        Assert.Contains("different mounts", job.Failure.Note, StringComparison.Ordinal);
        Assert.Equal(job.ArtefactName, EncodeFileName.Artefact(job.RecordingId, job.ProfileId));
    }

    [Fact]
    public async Task ARootThisProcessCannotPlaceRefusesTheJobWithoutTouchingTheDisk()
    {
        using var harness = new EncodeHarness();
        EncodeJob job = EncodeJob.Queue(
            EncodeJobId.New(), RecordingId.New(), EncodeProfileId.New(), EncodeDestinationId.New(), new OutputRoot("elsewhere"), EncodeHarness.Queued);
        job.Start(EncodeHarness.Started);

        EncodePlacementOutcome outcome = await harness.Placer.PlaceAsync(job, Cancel);

        Assert.Equal(EncodePlacementOutcome.Refused, outcome);
        Assert.Equal(EncodeFailure.CapabilityUnavailable, job.Failure!.Failure);
        Assert.Empty(harness.Shelf.Snapshot());
        Assert.DoesNotContain(harness.Jobs.Moves, move => move.StartsWith("claimed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OnlyARunningJobPlacesAnArtefact()
    {
        using var harness = new EncodeHarness();
        EncodeJob job = EncodeJob.Queue(
            EncodeJobId.New(), RecordingId.New(), EncodeProfileId.New(), EncodeDestinationId.New(), EncodeHarness.Primary, EncodeHarness.Queued);

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Placer.PlaceAsync(job, Cancel));
    }

    [Fact]
    public async Task AJobWhoseWorkFileIsNotWhereItWasToBeWrittenHasNothingToPlace()
    {
        using var harness = new EncodeHarness();
        EncodeJob job = harness.Running();

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Placer.PlaceAsync(job, Cancel));
        Assert.Equal(EncodeJobStatus.Running, job.Status);
    }
}
