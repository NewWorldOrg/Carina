using Carina.Api.Services;
using Carina.Domain.Recordings;

namespace Carina.Api.Tests.Unit.App.Recordings;

[Trait("Category", "Unit")]
public sealed class ThumbnailRemakesTests
{
    [Fact(DisplayName = "BR-KD-012: one request queues the pictures of at most a hundred recordings")]
    public void OneRequestQueuesAtMostAHundredPictures()
    {
        Assert.Equal(100, ThumbnailRemakes.MostAtOnce);
        Assert.Contains("100", ThumbnailRemakes.Description, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RecordingFailure.NoSuchRecording, ThumbnailRemakeOutcome.NoSuchRecording)]
    [InlineData(RecordingFailure.StillRecording, ThumbnailRemakeOutcome.StillRecording)]
    [InlineData(RecordingFailure.NowhereToPutPictures, ThumbnailRemakeOutcome.Refused)]
    [InlineData(RecordingFailure.FileOutOfReach, ThumbnailRemakeOutcome.Refused)]
    [InlineData(RecordingFailure.AlreadyEnded, ThumbnailRemakeOutcome.Refused)]
    [InlineData(RecordingFailure.NotBeingWritten, ThumbnailRemakeOutcome.Refused)]
    [InlineData(RecordingFailure.DriverUnreachable, ThumbnailRemakeOutcome.Refused)]
    [InlineData(RecordingFailure.DriverRefused, ThumbnailRemakeOutcome.Refused)]
    [InlineData(RecordingFailure.RootOutOfReach, ThumbnailRemakeOutcome.Refused)]
    [InlineData(RecordingFailure.FilesLeftBehind, ThumbnailRemakeOutcome.Refused)]
    [InlineData(RecordingFailure.OneIsAlreadyBeingDiscarded, ThumbnailRemakeOutcome.Refused)]
    [InlineData(RecordingFailure.TookTooLong, ThumbnailRemakeOutcome.Refused)]
    [InlineData(RecordingFailure.BeingEncoded, ThumbnailRemakeOutcome.Refused)]
    public void ARefusalOfTheSinglePathReadsAsTheOutcomeOfARow(RecordingFailure failure, ThumbnailRemakeOutcome outcome)
        => Assert.Equal(outcome, ThumbnailRemakes.Outcome(failure));

    [Fact]
    public void EveryFailureARecordingRequestCanHaveIsListedAbove()
    {
        RecordingFailure[] named =
        [
            RecordingFailure.NoSuchRecording,
            RecordingFailure.AlreadyEnded,
            RecordingFailure.NotBeingWritten,
            RecordingFailure.StillRecording,
            RecordingFailure.DriverUnreachable,
            RecordingFailure.DriverRefused,
            RecordingFailure.NowhereToPutPictures,
            RecordingFailure.FileOutOfReach,
            RecordingFailure.RootOutOfReach,
            RecordingFailure.FilesLeftBehind,
            RecordingFailure.OneIsAlreadyBeingDiscarded,
            RecordingFailure.TookTooLong,
            RecordingFailure.BeingEncoded,
        ];

        Assert.Equal(named.Order(), Enum.GetValues<RecordingFailure>().Order());
    }

    [Fact(DisplayName = "a row that queued the recording says nothing beside the outcome, and one that did not says why without naming the recording")]
    public void ARowSaysWhyNothingWasQueuedWithoutNamingTheRecording()
    {
        RecordingId id = RecordingId.New();

        ThumbnailRemakeRow queued = ThumbnailRemakeRow.Queued(id);
        ThumbnailRemakeRow missing = ThumbnailRemakeRow.NotQueued(id, $"There is no recording {id.Wire}.", RecordingFailure.NoSuchRecording);
        ThumbnailRemakeRow writing = ThumbnailRemakeRow.NotQueued(id, $"Recording {id.Wire} is still being written.", RecordingFailure.StillRecording);
        ThumbnailRemakeRow refused = ThumbnailRemakeRow.NotQueued(
            id,
            "Nothing tells this process where to put thumbnails.",
            RecordingFailure.NowhereToPutPictures);
        ThumbnailRemakeRow threw = ThumbnailRemakeRow.Threw(id);
        ThumbnailRemakeRow[] rows = [queued, missing, writing, refused, threw];

        Assert.All(rows, row => Assert.Equal(id, row.Id));
        Assert.Equal((ThumbnailRemakeOutcome.Queued, string.Empty), (queued.Outcome, queued.Message));
        Assert.Equal(ThumbnailRemakeOutcome.NoSuchRecording, missing.Outcome);
        Assert.Equal(ThumbnailRemakeOutcome.StillRecording, writing.Outcome);
        Assert.Equal((ThumbnailRemakeOutcome.Refused, "Nothing tells this process where to put thumbnails."), (refused.Outcome, refused.Message));
        Assert.Equal((ThumbnailRemakeOutcome.Refused, ThumbnailRemakes.SaidOfOneThatThrew), (threw.Outcome, threw.Message));
        Assert.All(rows.Skip(1), row => Assert.NotEmpty(row.Message));
        Assert.All(rows, row => Assert.DoesNotContain(id.Wire, row.Message, StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "the row of a recording whose queueing threw names neither the exception nor the recording")]
    public void TheMessageOfARowThatThrewIsFixed()
    {
        Assert.DoesNotContain("Exception", ThumbnailRemakes.SaidOfOneThatThrew, StringComparison.Ordinal);
        Assert.DoesNotContain("{", ThumbnailRemakes.SaidOfOneThatThrew, StringComparison.Ordinal);
        Assert.Contains("log", ThumbnailRemakes.SaidOfOneThatThrew, StringComparison.Ordinal);
    }
}
