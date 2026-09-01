using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Thumbnails;

namespace Carina.Domain.Tests.Thumbnails;

public sealed class ThumbnailSubjectTests
{
    private static readonly RecordingId Id = RecordingId.New();

    private static readonly OutputRoot Root = new("bulk");

    private static readonly RecordingFileName FileName = RecordingFileName.For(Id, ".m2ts");

    private static readonly ServiceId Service = new(1032);

    [Fact]
    public void ASubjectCarriesWhatTheLedgerSaysAboutTheRecording()
    {
        var subject = new ThumbnailSubject(Id, Root, FileName, Service, RecordingOutcome.Truncated, TimeSpan.FromMinutes(113));

        Assert.Equal(Id, subject.Id);
        Assert.Equal(Root, subject.Root);
        Assert.Equal(FileName, subject.FileName);
        Assert.Equal(RecordingOutcome.Truncated, subject.Outcome);
        Assert.Equal(TimeSpan.FromMinutes(113), subject.Written);
    }

    [Fact]
    public void ASubjectNamesTheRecordingItsRootAndItsFile()
    {
        Assert.Equal(
            "id",
            Assert.Throws<ArgumentNullException>(
                () => new ThumbnailSubject(null!, Root, FileName, Service, RecordingOutcome.Complete, TimeSpan.Zero)).ParamName);
        Assert.Equal(
            "root",
            Assert.Throws<ArgumentNullException>(
                () => new ThumbnailSubject(Id, null!, FileName, Service, RecordingOutcome.Complete, TimeSpan.Zero)).ParamName);
        Assert.Equal(
            "fileName",
            Assert.Throws<ArgumentNullException>(
                () => new ThumbnailSubject(Id, Root, null!, Service, RecordingOutcome.Complete, TimeSpan.Zero)).ParamName);
    }

    [Fact]
    public void AnOutcomeTheLedgerDoesNotHoldIsRefused()
        => Assert.Equal(
            "outcome",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ThumbnailSubject(Id, Root, FileName, Service, (RecordingOutcome)9, TimeSpan.Zero)).ParamName);

    [Fact]
    public void ARecordingShorterThanNothingIsRefused()
        => Assert.Equal(
            "written",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ThumbnailSubject(
                    Id,
                    Root,
                    FileName,
                    Service,
                    RecordingOutcome.Complete,
                    TimeSpan.FromMilliseconds(-1))).ParamName);

    [Fact]
    public void ASubjectNamesTheServiceThePictureHasToComeFrom()
        => Assert.Equal(
            "service",
            Assert.Throws<ArgumentNullException>(
                () => new ThumbnailSubject(
                    Id,
                    Root,
                    FileName,
                    null!,
                    RecordingOutcome.Complete,
                    TimeSpan.Zero)).ParamName);
}
