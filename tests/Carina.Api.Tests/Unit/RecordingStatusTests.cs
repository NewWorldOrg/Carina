using Carina.Api.Controllers.Recordings;
using Carina.Api.Services;

using Microsoft.AspNetCore.Http;

namespace Carina.Api.Tests.Unit;

public sealed class RecordingStatusTests
{
    [Theory]
    [InlineData(RecordingFailure.NoSuchRecording, StatusCodes.Status404NotFound)]
    [InlineData(RecordingFailure.AlreadyEnded, StatusCodes.Status409Conflict)]
    [InlineData(RecordingFailure.NotBeingWritten, StatusCodes.Status409Conflict)]
    [InlineData(RecordingFailure.StillRecording, StatusCodes.Status409Conflict)]
    [InlineData(RecordingFailure.DriverUnreachable, StatusCodes.Status503ServiceUnavailable)]
    [InlineData(RecordingFailure.DriverRefused, StatusCodes.Status502BadGateway)]
    [InlineData(RecordingFailure.NowhereToPutPictures, StatusCodes.Status503ServiceUnavailable)]
    [InlineData(RecordingFailure.FileOutOfReach, StatusCodes.Status503ServiceUnavailable)]
    [InlineData(RecordingFailure.RootOutOfReach, StatusCodes.Status409Conflict)]
    [InlineData(RecordingFailure.FilesLeftBehind, StatusCodes.Status503ServiceUnavailable)]
    [InlineData(RecordingFailure.OneIsAlreadyBeingDiscarded, StatusCodes.Status409Conflict)]
    public void EveryWayARecordingRequestCanFailIsAnsweredWithTheStatusItWasGiven(
        RecordingFailure failure,
        int status)
        => Assert.Equal(status, RecordingStatus.Of(failure));

    [Fact]
    public void EveryFailureThisEnumNamesIsListedAbove()
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
        ];

        Assert.Equal(Enum.GetValues<RecordingFailure>().Order().ToArray(), named.Order().ToArray());
    }

    [Fact]
    public void ARecordingThatIsStillBeingWrittenIsARefusalRatherThanAnAbsence()
    {
        Assert.NotEqual(
            RecordingStatus.Of(RecordingFailure.NoSuchRecording),
            RecordingStatus.Of(RecordingFailure.StillRecording));
        Assert.Equal(StatusCodes.Status409Conflict, RecordingStatus.Of(RecordingFailure.StillRecording));
    }

    [Fact]
    public void ARootThatCannotBeReachedRefusesTheDeletionRatherThanReportingTheServiceDown()
    {
        Assert.Equal(StatusCodes.Status409Conflict, RecordingStatus.Of(RecordingFailure.RootOutOfReach));
        Assert.NotEqual(
            RecordingStatus.Of(RecordingFailure.FileOutOfReach),
            RecordingStatus.Of(RecordingFailure.RootOutOfReach));
    }
}
