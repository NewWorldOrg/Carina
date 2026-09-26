using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Encodings;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class RecordingEncodesTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Ended = new(2026, 9, 5, 3, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AJobStillRunningIsWorkUnderWayOnItsRecording()
    {
        using EncodeHarness harness = new();
        RecordingId recording = RecordingId.New();
        harness.Running(recording);

        Assert.True(await Encodes(harness).AnyUnderWayAsync(recording, Cancel));
    }

    [Fact]
    public async Task AJobStillWaitingIsWorkUnderWayOnItsRecording()
    {
        using EncodeHarness harness = new();
        RecordingId recording = RecordingId.New();
        harness.Jobs.Jobs.Add(EncodeJob.Queue(
            EncodeJobId.New(),
            recording,
            EncodeProfileId.New(),
            EncodeDestinationId.New(),
            EncodeHarness.Encodes,
            EncodeHarness.Queued));

        Assert.True(await Encodes(harness).AnyUnderWayAsync(recording, Cancel));
    }

    [Fact]
    public async Task JobsThatEndedAndJobsOfAnotherRecordingAreNoWorkUnderWay()
    {
        using EncodeHarness harness = new();
        RecordingId recording = RecordingId.New();
        harness.Made(recording, EncodeProfileId.New());
        harness.Running();

        Assert.False(await Encodes(harness).AnyUnderWayAsync(recording, Cancel));
    }

    [Fact]
    public async Task WhatTheCompletedJobsOfARecordingMadeLeavesTheDiskAndNothingOfAnotherRecordingDoes()
    {
        using EncodeHarness harness = new();
        RecordingId recording = RecordingId.New();
        EncodeJob small = harness.Made(recording, EncodeProfileId.New());
        EncodeJob large = harness.Made(recording, EncodeProfileId.New());
        EncodeJob another = harness.Made(RecordingId.New(), EncodeProfileId.New());
        string[] made = [.. new[] { small, large, another }.Select(job => Holding(harness, job))];

        EncodesErased erasure = await Encodes(harness).EraseAsync(recording, Cancel);

        Assert.True(erasure.EverythingIsGone);
        Assert.Equal(2, erasure.FilesRemoved);
        Assert.False(File.Exists(made[0]));
        Assert.False(File.Exists(made[1]));
        Assert.True(File.Exists(made[2]), "the artefact of another recording was taken off the disk");
    }

    [Fact]
    public async Task AnArtefactMadeAgainUnderTheSameNameIsRemovedOnce()
    {
        using EncodeHarness harness = new();
        RecordingId recording = RecordingId.New();
        EncodeProfileId profile = EncodeProfileId.New();
        EncodeJob first = harness.Made(recording, profile);
        first.GiveUpTheName(Ended);
        EncodeJob again = harness.Made(recording, profile);
        string artefact = Holding(harness, again);

        EncodesErased erasure = await Encodes(harness).EraseAsync(recording, Cancel);

        Assert.Equal(1, erasure.FilesRemoved);
        Assert.False(File.Exists(artefact));
    }

    [Fact]
    public async Task AFileAtTheNameOfAJobThatFailedIsLeftWhereItIs()
    {
        using EncodeHarness harness = new();
        RecordingId recording = RecordingId.New();
        EncodeJob collided = harness.Running(recording);
        collided.Name(EncodeFileName.Artefact(recording, collided.ProfileId));
        collided.Fail(EncodePlacements.WhatACollisionIsCalled, "something no job wrote is already there", Ended);
        string foreign = Holding(harness, collided);

        EncodesErased erasure = await Encodes(harness).EraseAsync(recording, Cancel);

        Assert.True(erasure.EverythingIsGone);
        Assert.Equal(0, erasure.FilesRemoved);
        Assert.True(File.Exists(foreign), "a file no job of this recording made was taken off the disk");
    }

    [Fact]
    public async Task WhatAnEndedJobStillOwesARemovalForIsSweptWithIt()
    {
        using EncodeHarness harness = new();
        RecordingId recording = RecordingId.New();
        EncodeJob failed = harness.Running(recording);
        string work = harness.WorkFileOf(failed, "half a picture");
        failed.Fail(EncodeFailure.FfmpegExitedNonZero, "exit 1", Ended);

        EncodesErased erasure = await Encodes(harness).EraseAsync(recording, Cancel);

        Assert.Equal(1, erasure.FilesRemoved);
        Assert.False(File.Exists(work));
        Assert.Equal(EncodeScratchFate.Removed, Assert.Single(harness.Scratch.Files).Fate);
    }

    [Fact]
    public async Task AFileThatCouldNotBeRemovedWhenItsJobEndedIsTriedAgain()
    {
        using EncodeHarness harness = new();
        RecordingId recording = RecordingId.New();
        EncodeJob failed = harness.Running(recording);
        string work = harness.WorkFileOf(failed, "half a picture");
        failed.Fail(EncodeFailure.FfmpegExitedNonZero, "exit 1", Ended);
        harness.Scratch.Files[0].Settle(EncodeScratchFate.CouldNotBeRemoved, Ended);

        EncodesErased erasure = await Encodes(harness).EraseAsync(recording, Cancel);

        Assert.True(erasure.EverythingIsGone);
        Assert.Equal(1, erasure.FilesRemoved);
        Assert.False(File.Exists(work));
        Assert.Equal(EncodeScratchFate.Removed, harness.Scratch.Files[0].Fate);
    }

    [Fact]
    public async Task AFileThatStillCannotBeRemovedIsLeftBehindEveryTimeItIsAskedFor()
    {
        using EncodeHarness harness = new();
        RecordingId recording = RecordingId.New();
        EncodeJob failed = harness.Running(recording);
        harness.WorkFileOf(failed, "half a picture");
        failed.Fail(EncodeFailure.FfmpegExitedNonZero, "exit 1", Ended);
        harness.Settings = new EncodeSettings { OutputRoots = [new StorageRootPath(new OutputRoot("elsewhere"), harness.Shelf.Root)] };

        EncodesErased first = await Encodes(harness).EraseAsync(recording, Cancel);
        EncodesErased again = await Encodes(harness).EraseAsync(recording, Cancel);

        Assert.Equal([failed.WorkFileName], first.Left);
        Assert.Equal([failed.WorkFileName], again.Left);
    }

    [Fact]
    public async Task AnArtefactAlreadyGoneIsNotAFileLeftBehind()
    {
        using EncodeHarness harness = new();
        RecordingId recording = RecordingId.New();
        harness.Made(recording, EncodeProfileId.New());

        EncodesErased erasure = await Encodes(harness).EraseAsync(recording, Cancel);

        Assert.True(erasure.EverythingIsGone);
        Assert.Equal(0, erasure.FilesRemoved);
    }

    [Fact]
    public async Task AnArtefactUnderARootThisProcessDoesNotHoldIsAFileLeftBehind()
    {
        using EncodeHarness harness = new();
        RecordingId recording = RecordingId.New();
        EncodeJob made = harness.Made(recording, EncodeProfileId.New());
        harness.Settings = new EncodeSettings { OutputRoots = [new StorageRootPath(new OutputRoot("elsewhere"), harness.Shelf.Root)] };

        EncodesErased erasure = await Encodes(harness).EraseAsync(recording, Cancel);

        Assert.False(erasure.EverythingIsGone);
        Assert.Equal(EncodeFileName.Artefact(recording, made.ProfileId), Assert.Single(erasure.Left));
    }

    private static string Holding(EncodeHarness harness, EncodeJob job)
    {
        string path = harness.ArtefactPathOf(job);
        File.WriteAllText(path, "a picture");

        return path;
    }

    private static RecordingEncodes Encodes(EncodeHarness harness)
        => new(harness.Jobs, harness.Cleaner, NullLogger<RecordingEncodes>.Instance);
}
