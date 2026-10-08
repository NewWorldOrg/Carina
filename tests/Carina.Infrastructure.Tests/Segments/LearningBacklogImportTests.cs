using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Segments;
using Carina.TestSupport;

using static Carina.Infrastructure.Tests.Segments.LearningFollowHarness;

namespace Carina.Infrastructure.Tests.Segments;

public sealed class LearningBacklogImportTests : IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly ExtractionVersion Reduced = ExtractionVersion.CurrentFromReducedCopy;

    private readonly LearningBacklogHarness harness = new();

    public void Dispose() => harness.Dispose();

    [Fact(DisplayName = "in spare time a reduced copy is imported before any recording is read, its record reading with the version of a reduced copy and the copy of the programme its rows give")]
    public async Task InSpareTimeACopyIsImportedBeforeAnyRecordingIsRead()
    {
        await harness.LearningAsync(true);
        harness.Ended(40, 7701);
        CopyDescription copied = harness.Copied("one");
        LearningBacklogJob job = harness.Job();

        LearningBacklogLook look = await job.LookAsync(Cancel);

        Assert.Equal((copied.Id, null, copied.Id), (look.Importing, look.Began, look.Reading));
        LearningExtraction record = Assert.IsType<LearningExtraction>(harness.Records.Row(copied.Id));
        Assert.Equal((LearningExtractionState.Reading, Reduced), (record.State, record.Version));
        Assert.Equal(ReducedCopyReader.Read(harness.Copies.Under("one")).Copy?.Programme, record.Programme);
        await Eventually.Happens(() => !harness.Importer.Asked.IsEmpty, "the import never began");
        Assert.Equal(copied.Id, Assert.Single(harness.Importer.Asked).Id);
        Assert.Empty(harness.Reader.Asked);
    }

    [Fact(DisplayName = "one copy or recording at a time: a recording is read once the import before it has settled")]
    public async Task ARecordingIsReadOnceTheImportBeforeItHasSettled()
    {
        await harness.LearningAsync(true);
        Recording recording = harness.Ended(40, 7702);
        CopyDescription copied = harness.Copied("one");
        LearningBacklogJob job = harness.Job();
        await job.LookAsync(Cancel);

        LearningBacklogLook meanwhile = await job.LookAsync(Cancel);

        Assert.Equal((null, null, copied.Id), (meanwhile.Importing, meanwhile.Began, meanwhile.Reading));
        Assert.Null(harness.Records.Row(recording.Id));

        harness.Importer.Release(copied.Id);
        LearningBacklogLook after = await Eventually.Yields(
            async () => await job.LookAsync(Cancel),
            look => look.Began is not null,
            look => $"still on {look.Reading?.Wire}",
            "the recording was never read");

        Assert.Equal((LearningExtractionState.Done, Reduced), (harness.Records.Row(copied.Id)?.State, harness.Records.Row(copied.Id)?.Version));
        Assert.Equal(recording.Id, after.Began);
        Assert.Single(harness.Records.All(), record => record.State is LearningExtractionState.Reading);
    }

    [Fact(DisplayName = "as many recordings are imported as there are copies, and a second start imports none of them again")]
    public async Task AsManyAreImportedAsThereAreCopiesAndASecondStartImportsNone()
    {
        await harness.LearningAsync(true);
        harness.Importer.ReleasesAtOnce = true;
        CopyDescription[] copied = [harness.Copied("a"), harness.Copied("b"), harness.Copied("c")];

        await LookUntilIdleAsync(harness.Job());

        Assert.Equal(copied.Select(copy => copy.Id.Value).Order(), harness.Importer.Asked.Select(copy => copy.Id.Value).Order());
        Assert.All(copied, copy => Assert.Equal((LearningExtractionState.Done, Reduced), (harness.Records.Row(copy.Id)?.State, harness.Records.Row(copy.Id)?.Version)));

        int saves = harness.Records.Saves;
        LearningBacklogJob restarted = harness.Job();
        LearningBacklogLook look = await restarted.LookAsync(Cancel);

        Assert.Equal((null, null), (look.Importing, look.Reading));
        Assert.Equal(3, harness.Importer.Asked.Count);
        Assert.Equal(3, harness.Records.All().Count);
        Assert.Equal(saves, harness.Records.Saves);
    }

    [Fact(DisplayName = "a copy of a recording whose row and file are kept is read again from the file once it is imported")]
    public async Task ACopyOfARecordingStillKeptIsReadAgainFromItsFile()
    {
        await harness.LearningAsync(true);
        Recording recording = harness.Ended(40, 7703);
        harness.Copied("kept", recording);
        LearningBacklogJob job = harness.Job();

        Assert.Equal(recording.Id, (await job.LookAsync(Cancel)).Importing);

        harness.Importer.Release(recording.Id);
        LearningBacklogLook after = await Eventually.Yields(
            async () => await job.LookAsync(Cancel),
            look => look.Began is not null,
            look => $"still on {look.Reading?.Wire}",
            "the recording was never read again");

        Assert.Equal(recording.Id, after.Began);
        Assert.Equal((LearningExtractionState.Reading, ExtractionVersion.Current), (harness.Records.Row(recording.Id)?.State, harness.Records.Row(recording.Id)?.Version));
        await Eventually.Happens(() => !harness.Reader.Asked.IsEmpty, "the reader was asked to read the recording");
        Assert.Equal(recording.Id, Assert.Single(harness.Reader.Asked).Id);
    }

    [Fact(DisplayName = "a copy of a recording whose data was already read from its file is not imported")]
    public async Task ACopyOfARecordingAlreadyReadIsNotImported()
    {
        await harness.LearningAsync(true);
        Recording recording = harness.Ended(40, 7704);
        LearningExtraction read = LearningExtraction.Waiting(recording.Id, ProgrammeCopy.Of(recording, null), Now);
        read.Read(ExtractionVersion.Current, Now);
        read.Finish(Now);
        harness.Records.Hold(read);
        harness.Copied("read", recording);

        LearningBacklogLook look = await harness.Job().LookAsync(Cancel);

        Assert.Equal((null, null, null), (look.Importing, look.Began, look.Reading));
        Assert.Empty(harness.Importer.Asked);
        Assert.Equal((LearningExtractionState.Done, ExtractionVersion.Current), (harness.Records.Row(recording.Id)?.State, harness.Records.Row(recording.Id)?.Version));
    }

    [Fact(DisplayName = "a copy of another shape or lacking a file is not imported, and the rest are")]
    public async Task ACopyOfAnotherShapeOrLackingAFileIsNotImported()
    {
        await harness.LearningAsync(true);
        harness.Importer.ReleasesAtOnce = true;
        CopyDescription whole = harness.Copied("whole");
        CopyDescription lacking = harness.Copied("lacking", null, ReducedCopy.Frames);
        CopyDescription other = new() { Shape = "another-shape" };
        ReducedCopies.Write(harness.Copies.Root, "other", other);

        await LookUntilIdleAsync(harness.Job());

        Assert.Equal(whole.Id, Assert.Single(harness.Importer.Asked).Id);
        Assert.Null(harness.Records.Row(lacking.Id));
        Assert.Null(harness.Records.Row(other.Id));
    }

    [Fact(DisplayName = "a copy whose probe gives a time no file's clock could hold is not imported, and the look goes on to the next copy")]
    public async Task ACopyWithATimeOutOfReachIsPassedOver()
    {
        await harness.LearningAsync(true);
        harness.Importer.ReleasesAtOnce = true;
        CopyDescription vast = new() { FileBegins = "1e30" };
        CopyDescription far = new() { PictureBegins = "1e13", SoundBegins = "1e13" };
        ReducedCopies.Write(harness.Copies.Root, "a-vast", vast);
        ReducedCopies.Write(harness.Copies.Root, "b-far", far);
        CopyDescription whole = harness.Copied("c-whole");

        LearningBacklogLook look = await harness.Job().LookAsync(Cancel);

        Assert.Equal(whole.Id, look.Importing);
        Assert.Null(harness.Records.Row(vast.Id));
        Assert.Null(harness.Records.Row(far.Id));
    }

    [Fact(DisplayName = "a copy that failed to import is not tried again until the next start, and is tried again then")]
    public async Task ACopyThatFailedIsTriedAgainAtTheNextStart()
    {
        await harness.LearningAsync(true);
        harness.Importer.ReleasesAtOnce = true;
        harness.Importer.Fails = true;
        CopyDescription copied = harness.Copied("failing");

        await LookUntilIdleAsync(harness.Job());

        Assert.Single(harness.Importer.Asked);
        Assert.Equal((LearningExtractionState.Failed, 1), (harness.Records.Row(copied.Id)?.State, harness.Records.Row(copied.Id)?.Failures));

        harness.Importer.Fails = false;
        await LookUntilIdleAsync(harness.Job());

        Assert.Equal(2, harness.Importer.Asked.Count);
        Assert.Equal(LearningExtractionState.Done, harness.Records.Row(copied.Id)?.State);
    }

    [Fact(DisplayName = "switching learning off stops the import within a look and puts its record back to wait, and it is imported again once learning is back on")]
    public async Task SwitchingLearningOffStopsTheImport()
    {
        await harness.LearningAsync(true);
        CopyDescription copied = harness.Copied("stopped");
        LearningBacklogJob job = harness.Job();
        await job.LookAsync(Cancel);
        await Eventually.Happens(() => !harness.Importer.Asked.IsEmpty, "the import never began");

        await harness.LearningAsync(false);
        LearningBacklogLook look = await job.LookAsync(Cancel);

        Assert.Equal((copied.Id, null, SpareTimeVerdict.LearningOff), (look.Stopped, look.Reading, look.Verdict));
        Assert.Equal([copied.Id], harness.Importer.Stopped);
        Assert.Equal(LearningExtractionState.Waiting, harness.Records.Row(copied.Id)?.State);

        await harness.LearningAsync(true);

        Assert.Equal(copied.Id, (await job.LookAsync(Cancel)).Importing);
    }

    [Fact(DisplayName = "outside spare time no copy is imported")]
    public async Task OutsideSpareTimeNoCopyIsImported()
    {
        await harness.LearningAsync(true);
        harness.BeingRecorded(7705);
        CopyDescription copied = harness.Copied("waiting");

        LearningBacklogLook look = await harness.Job().LookAsync(Cancel);

        Assert.Equal((SpareTimeVerdict.Recording, null), (look.Verdict, look.Importing));
        Assert.Null(harness.Records.Row(copied.Id));
    }

    [Fact(DisplayName = "with no directory to import from, no copy is imported and the recordings are read as before")]
    public async Task WithNoDirectoryNoCopyIsImported()
    {
        await harness.LearningAsync(true);
        Recording recording = harness.Ended(40, 7706);
        harness.Copied("unread");

        LearningBacklogLook look = await harness.Job(import: LearningImportSettings.None).LookAsync(Cancel);

        Assert.Equal((null, recording.Id), (look.Importing, look.Began));
        Assert.Single(harness.Records.All());
    }

    [Fact(DisplayName = "nothing on the shelf of copies is written, moved or removed")]
    public async Task NothingOnTheShelfIsTouched()
    {
        await harness.LearningAsync(true);
        harness.Importer.ReleasesAtOnce = true;
        harness.Copied("a");
        harness.Copied("b", null, ReducedCopy.Corners);
        IReadOnlyList<string> before = harness.Copies.Snapshot();

        await LookUntilIdleAsync(harness.Job());

        Assert.Equal(before, harness.Copies.Snapshot());
    }

    private static async Task LookUntilIdleAsync(LearningBacklogJob job)
    {
        for (int look = 0; look < 50; look++)
        {
            LearningBacklogLook looked = await job.LookAsync(Cancel);

            if (looked is { Reading: null, Importing: null, Began: null })
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        Assert.Fail("the look never came to rest");
    }
}
