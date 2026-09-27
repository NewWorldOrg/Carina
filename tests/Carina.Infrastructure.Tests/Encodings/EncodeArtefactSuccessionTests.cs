using System.Text;

using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Encodings;
using Carina.Infrastructure.Tests.Integrity;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class EncodeArtefactSuccessionTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime MadeFirst = new(2026, 9, 5, 3, 10, 0, DateTimeKind.Utc);

    private static readonly DateTime MadeAgain = new(2026, 9, 5, 3, 40, 0, DateTimeKind.Utc);

    private static readonly OutputRoot Annex = new("annex");

    [Fact(DisplayName = "once a recording is made again under another profile, what the earlier job made is replaced and its file removed, and the recording itself is left as it was")]
    public async Task WhatTheEarlierJobMadeUnderAnotherNameIsRemoved()
    {
        using EncodeHarness harness = new();
        Recording recording = harness.Recorded();
        EncodeJob earlier = Completed(harness, recording.Id, EncodeHarness.Encodes, MadeFirst, "the first picture");
        EncodeJob newer = Completed(harness, recording.Id, EncodeHarness.Encodes, MadeAgain, "the second picture");

        EncodeSuccessionReport report = await harness.Succession.SettleAsync(recording.Id, Cancel);

        Assert.Equal(1, report.Replaced);
        Assert.Equal(1, report.FilesRemoved);
        Assert.Empty(report.Left);
        Assert.False(File.Exists(harness.ArtefactPathOf(earlier)));
        Assert.Equal("the second picture", File.ReadAllText(harness.ArtefactPathOf(newer)));
        Assert.Equal(EncodeHarness.Broadcast, File.ReadAllText(harness.SourcePathOf(recording)));
        Assert.Equal(harness.Clock.GetUtcNow().UtcDateTime, earlier.ReplacedAt);
        Assert.Null(newer.ReplacedAt);
        Assert.True(newer.StandsAsTheArtefact);

        EncodeScratchFile owed = Assert.Single(harness.Scratch.Files);
        Assert.Equal(EncodeScratchKind.ReplacedArtefact, owed.Kind);
        Assert.Equal(earlier.Id, owed.JobId);
        Assert.Equal(earlier.ArtefactName, owed.FileName);
        Assert.Equal(EncodeScratchFate.Removed, owed.Fate);
        Assert.Equal(
            [$"recorded {earlier.ArtefactName!.Value}", $"settled {earlier.ArtefactName.Value} Removed"],
            harness.Scratch.Moves);
        Assert.Equal([$"saved {earlier.Id.Wire} Completed"], harness.Jobs.Moves);
    }

    [Fact(DisplayName = "an earlier artefact at the very name the newer one was renamed onto is replaced in the ledger only, and the file there, the newer one, is not touched")]
    public async Task AnArtefactAtTheSameNameIsReplacedInTheLedgerOnly()
    {
        using EncodeHarness harness = new();
        Recording recording = harness.Recorded();
        EncodeProfileId profile = EncodeProfileId.New();
        EncodeJob earlier = Completed(harness, recording.Id, EncodeHarness.Encodes, MadeFirst, null, profile);
        EncodeJob newer = Completed(harness, recording.Id, EncodeHarness.Encodes, MadeAgain, "the second picture", profile);

        EncodeSuccessionReport report = await harness.Succession.SettleAsync(recording.Id, Cancel);

        Assert.Equal(1, report.Replaced);
        Assert.Equal(0, report.FilesRemoved);
        Assert.Equal("the second picture", File.ReadAllText(harness.ArtefactPathOf(newer)));
        Assert.NotNull(earlier.ReplacedAt);
        Assert.Empty(harness.Scratch.Files);
    }

    [Theory(DisplayName = "a newer job that failed or was called off replaces nothing, and the earlier artefact stays the recording's one")]
    [InlineData(EncodeJobStatus.Failed)]
    [InlineData(EncodeJobStatus.Cancelled)]
    public async Task ANewerJobThatDidNotCompleteReplacesNothing(EncodeJobStatus ending)
    {
        using EncodeHarness harness = new();
        Recording recording = harness.Recorded();
        EncodeJob earlier = Completed(harness, recording.Id, EncodeHarness.Encodes, MadeFirst, "the first picture");
        EncodeJob newer = harness.RunningAgain(recording.Id, EncodeProfileId.New());

        if (ending is EncodeJobStatus.Failed)
        {
            newer.Fail(EncodeFailure.FfmpegExitedNonZero, "exit 1", MadeAgain);
        }
        else
        {
            newer.Cancel(MadeAgain);
        }

        EncodeSuccessionReport report = await harness.Succession.SettleAsync(recording.Id, Cancel);

        Assert.Equal(0, report.Replaced);
        Assert.Equal(0, report.FilesRemoved);
        Assert.Null(earlier.ReplacedAt);
        Assert.True(earlier.StandsAsTheArtefact);
        Assert.Equal("the first picture", File.ReadAllText(harness.ArtefactPathOf(earlier)));
        Assert.Empty(harness.Scratch.Files);
        Assert.Empty(harness.Jobs.Moves);
    }

    [Fact(DisplayName = "an earlier file that cannot be removed stays owed in the ledger, is said aloud, and goes at the next settling")]
    public async Task AFileThatCannotBeRemovedStaysOwedAndGoesAtTheNextSettling()
    {
        using EncodeHarness harness = new();
        using TempTree annex = new();
        Recording recording = harness.Recorded();
        EncodeJob earlier = Completed(harness, recording.Id, Annex, MadeFirst, null);
        string left = Path.Combine(annex.Root, earlier.ArtefactName!.Value);
        File.WriteAllText(left, "the first picture");
        Completed(harness, recording.Id, EncodeHarness.Encodes, MadeAgain, "the second picture");

        EncodeSuccessionReport first = await harness.Succession.SettleAsync(recording.Id, Cancel);

        Assert.Equal(1, first.Replaced);
        Assert.Equal([earlier.ArtefactName], first.Left);
        Assert.True(File.Exists(left));
        Assert.True(Assert.Single(harness.Scratch.Files).IsOwedARemoval);
        Assert.Equal(EncodeScratchFate.CouldNotBeRemoved, harness.Scratch.Files[0].Fate);
        Assert.Single(harness.SuccessionLog.Warnings);

        harness.Settings = new EncodeSettings
        {
            OutputRoots = [.. harness.Settings.OutputRoots, new StorageRootPath(Annex, annex.Root)],
            StalledAfter = harness.Settings.StalledAfter,
        };

        EncodeSuccessionReport again = await harness.Succession.SettleAsync(recording.Id, Cancel);

        Assert.Equal(0, again.Replaced);
        Assert.Equal(1, again.FilesRemoved);
        Assert.Empty(again.Left);
        Assert.False(File.Exists(left));
        Assert.Equal(EncodeScratchFate.Removed, Assert.Single(harness.Scratch.Files).Fate);
    }

    [Fact(DisplayName = "a removal still owed at the name the standing artefact now holds is settled without touching the disk, so the standing artefact is never removed")]
    public async Task ARemovalOwedAtTheStandingNameIsSettledWithoutTouchingTheDisk()
    {
        using EncodeHarness harness = new();
        Recording recording = harness.Recorded();
        EncodeProfileId profile = EncodeProfileId.New();
        EncodeJob first = Replaced(harness, recording.Id, profile, MadeFirst);
        EncodeScratchFile stillOwed = EncodeScratchFile.Record(
            EncodeScratchFileId.New(),
            first.Id,
            EncodeScratchKind.ReplacedArtefact,
            first.OutputRoot,
            first.ArtefactName!,
            MadeFirst.AddMinutes(1));
        stillOwed.Settle(EncodeScratchFate.CouldNotBeRemoved, MadeFirst.AddMinutes(1));
        harness.Scratch.Files.Add(stillOwed);
        EncodeJob standing = Completed(harness, recording.Id, EncodeHarness.Encodes, MadeAgain, "the third picture", profile);

        EncodeSuccessionReport report = await harness.Succession.SettleAsync(recording.Id, Cancel);

        Assert.Equal(0, report.Replaced);
        Assert.Equal(0, report.FilesRemoved);
        Assert.Empty(report.Left);
        Assert.Equal("the third picture", File.ReadAllText(harness.ArtefactPathOf(standing)));
        Assert.Equal(EncodeScratchFate.BecameTheArtefact, stillOwed.Fate);
        Assert.False(stillOwed.IsOwedARemoval);
    }

    [Fact(DisplayName = "someone already reading the earlier artefact when it is removed reads the one they opened through to its end")]
    public async Task SomeoneReadingTheEarlierArtefactReadsItToTheEnd()
    {
        using EncodeHarness harness = new();
        Recording recording = harness.Recorded();
        EncodeJob earlier = Completed(harness, recording.Id, EncodeHarness.Encodes, MadeFirst, "the first picture");
        Completed(harness, recording.Id, EncodeHarness.Encodes, MadeAgain, "the second picture");

        await using FileStream watching = new(
            harness.ArtefactPathOf(earlier),
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.ReadWrite | FileShare.Delete,
                BufferSize = 0,
            });

        byte[] opening = new byte[4];
        await watching.ReadExactlyAsync(opening, Cancel);

        await harness.Succession.SettleAsync(recording.Id, Cancel);

        byte[] rest = new byte[13];
        await watching.ReadExactlyAsync(rest, Cancel);

        Assert.False(File.Exists(harness.ArtefactPathOf(earlier)));
        Assert.Equal("the ", Encoding.UTF8.GetString(opening));
        Assert.Equal("first picture", Encoding.UTF8.GetString(rest));
    }

    [Fact(DisplayName = "of three completed jobs only the last stands, and settling once more changes nothing")]
    public async Task OfThreeCompletedJobsOnlyTheLastStands()
    {
        using EncodeHarness harness = new();
        Recording recording = harness.Recorded();
        EncodeJob first = Completed(harness, recording.Id, EncodeHarness.Encodes, MadeFirst, "the first picture");
        EncodeJob second = Completed(harness, recording.Id, EncodeHarness.Encodes, MadeFirst.AddMinutes(10), "the second picture");
        EncodeJob third = Completed(harness, recording.Id, EncodeHarness.Encodes, MadeAgain, "the third picture");

        await harness.Succession.SettleAsync(recording.Id, Cancel);
        EncodeSuccessionReport again = await harness.Succession.SettleAsync(recording.Id, Cancel);

        Assert.Equal(0, again.Replaced);
        Assert.Equal(0, again.FilesRemoved);
        Assert.NotNull(first.ReplacedAt);
        Assert.NotNull(second.ReplacedAt);
        Assert.True(third.StandsAsTheArtefact);
        Assert.Equal([harness.ArtefactPathOf(third)], Directory.GetFiles(harness.Shelf.Root));
    }

    private static EncodeJob Completed(
        EncodeHarness harness,
        RecordingId recording,
        OutputRoot root,
        DateTime ended,
        string? picture,
        EncodeProfileId? profile = null)
    {
        EncodeProfileId made = profile ?? EncodeProfileId.New();
        EncodeJob job = EncodeJob.Rehydrate(
            EncodeJobId.New(),
            recording,
            made,
            EncodeDestinationId.New(),
            root,
            EncodeJobStatus.Completed,
            EncodeJob.FirstAttempt,
            EncodeHarness.Queued,
            EncodeHarness.Started,
            ended,
            null,
            EncodeFileName.Artefact(recording, made),
            null,
            null,
            null,
            null,
            null);
        harness.Jobs.Jobs.Add(job);

        if (picture is not null)
        {
            File.WriteAllText(harness.ArtefactPathOf(job), picture);
        }

        return job;
    }

    private static EncodeJob Replaced(EncodeHarness harness, RecordingId recording, EncodeProfileId profile, DateTime ended)
    {
        EncodeJob job = EncodeJob.Rehydrate(
            EncodeJobId.New(),
            recording,
            profile,
            EncodeDestinationId.New(),
            EncodeHarness.Encodes,
            EncodeJobStatus.Completed,
            EncodeJob.FirstAttempt,
            EncodeHarness.Queued,
            EncodeHarness.Started,
            ended,
            null,
            EncodeFileName.Artefact(recording, profile),
            null,
            null,
            null,
            null,
            null,
            makesItAgain: false,
            nameGivenUpAt: ended.AddMinutes(1),
            replacedAt: ended.AddMinutes(1));
        harness.Jobs.Jobs.Add(job);

        return job;
    }
}
