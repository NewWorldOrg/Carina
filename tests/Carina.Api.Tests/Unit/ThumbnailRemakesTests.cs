using Carina.Api.Services;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Thumbnails;

namespace Carina.Api.Tests.Unit;

public sealed class ThumbnailRemakesTests
{
    [Fact(DisplayName = "BR-KD-012: one request remakes the pictures of at most a hundred recordings")]
    public void OneRequestRemakesAtMostAHundredPictures()
    {
        Assert.Equal(100, ThumbnailRemakes.MostAtOnce);
        Assert.Contains("100", ThumbnailRemakes.Description, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ThumbnailRemake.Drawn, ThumbnailRemakeOutcome.Drawn)]
    [InlineData(ThumbnailRemake.Skipped, ThumbnailRemakeOutcome.Skipped)]
    [InlineData(ThumbnailRemake.Failed, ThumbnailRemakeOutcome.Failed)]
    public void AnAnswerThePassRecordsIsTheOutcomeOfTheRow(ThumbnailRemake remake, ThumbnailRemakeOutcome outcome)
        => Assert.Equal(outcome, ThumbnailRemakes.Outcome(remake));

    [Theory]
    [InlineData(ThumbnailRemake.NothingToAskAbout)]
    [InlineData(ThumbnailRemake.NowhereToPutThem)]
    [InlineData(ThumbnailRemake.OutOfReach)]
    public void AnAnswerThatDrewNothingIsNeverTheOutcomeOfARowThatRemade(ThumbnailRemake remake)
        => Assert.Throws<ArgumentOutOfRangeException>(() => ThumbnailRemakes.Outcome(remake));

    [Fact]
    public void EveryAnswerTheRemakeCanGiveIsListedAbove()
        => Assert.Equal(
            [
                ThumbnailRemake.NothingToAskAbout,
                ThumbnailRemake.NowhereToPutThem,
                ThumbnailRemake.OutOfReach,
                ThumbnailRemake.Drawn,
                ThumbnailRemake.Skipped,
                ThumbnailRemake.Failed,
            ],
            Enum.GetValues<ThumbnailRemake>());

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

    [Fact(DisplayName = "a row that drew a picture says nothing beside the outcome, and one that did not says why without naming the recording")]
    public void ARowSaysWhyNothingWasDrawnWithoutNamingTheRecording()
    {
        RecordingId id = RecordingId.New();

        ThumbnailRemakeRow drawn = ThumbnailRemakeRow.Remade(id, ThumbnailRemake.Drawn);
        ThumbnailRemakeRow skipped = ThumbnailRemakeRow.Remade(id, ThumbnailRemake.Skipped);
        ThumbnailRemakeRow failed = ThumbnailRemakeRow.Remade(id, ThumbnailRemake.Failed);
        ThumbnailRemakeRow missing = ThumbnailRemakeRow.NotRemade(id, $"There is no recording {id.Wire}.", RecordingFailure.NoSuchRecording);
        ThumbnailRemakeRow writing = ThumbnailRemakeRow.NotRemade(id, $"Recording {id.Wire} is still being written.", RecordingFailure.StillRecording);
        ThumbnailRemakeRow refused = ThumbnailRemakeRow.NotRemade(
            id,
            "Nothing tells this process where to put thumbnails.",
            RecordingFailure.NowhereToPutPictures);
        ThumbnailRemakeRow[] rows = [drawn, skipped, failed, missing, writing, refused];

        Assert.All(rows, row => Assert.Equal(id, row.Id));
        Assert.Equal((ThumbnailRemakeOutcome.Drawn, string.Empty), (drawn.Outcome, drawn.Message));
        Assert.Equal(ThumbnailRemakeOutcome.Skipped, skipped.Outcome);
        Assert.Equal(ThumbnailRemakeOutcome.Failed, failed.Outcome);
        Assert.Equal(ThumbnailRemakeOutcome.NoSuchRecording, missing.Outcome);
        Assert.Equal(ThumbnailRemakeOutcome.StillRecording, writing.Outcome);
        Assert.Equal((ThumbnailRemakeOutcome.Refused, "Nothing tells this process where to put thumbnails."), (refused.Outcome, refused.Message));
        Assert.All(rows.Skip(1), row => Assert.NotEmpty(row.Message));
        Assert.All(rows, row => Assert.DoesNotContain(id.Wire, row.Message, StringComparison.OrdinalIgnoreCase));
    }
}
