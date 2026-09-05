using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Encodings;

public sealed class EncodeFileNameTests
{
    private static readonly RecordingId Recording = new(Guid.Parse("1872e6a8-80e9-4ac6-a8f9-3f740239ef00"));

    private static readonly EncodeJobId Job = new(Guid.Parse("5b1f2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d"));

    private static readonly EncodeProfileId Profile = new(Guid.Parse("0a1b2c3d-4e5f-4061-8283-8485868788a9"));

    [Fact(DisplayName = "BR-ED2-009: a work file is named for the recording, the job and the attempt")]
    public void AWorkFileIsNamedForTheRecordingTheJobAndTheAttempt()
    {
        EncodeFileName working = EncodeFileName.Working(Recording, Job, 3);

        Assert.Equal(
            "1872e6a880e94ac6a8f93f740239ef00.5b1f2c3d4e5f4a6b8c7d9e0f1a2b3c4d.attempt3.encoding",
            working.Value);
        Assert.True(working.Names(Recording));
        Assert.True(working.Names(Job));
    }

    [Fact(DisplayName = "BR-ED2-009: two attempts of one job, and two jobs on one recording, never share a work file")]
    public void TwoAttemptsOfOneJobAndTwoJobsOnOneRecordingNeverShareAWorkFile()
    {
        EncodeFileName first = EncodeFileName.Working(Recording, Job, 1);
        EncodeFileName second = EncodeFileName.Working(Recording, Job, 2);
        EncodeFileName another = EncodeFileName.Working(Recording, EncodeJobId.New(), 1);

        Assert.NotEqual(first, second);
        Assert.NotEqual(first, another);
        Assert.NotEqual(second, another);
    }

    [Fact(DisplayName = "BR-ED2-009: an attempt before the first names no work file")]
    public void AnAttemptBeforeTheFirstNamesNoWorkFile()
        => Assert.Throws<ArgumentOutOfRangeException>(() => EncodeFileName.Working(Recording, Job, 0));

    [Fact(DisplayName = "BR-ED2-009: the artefact is named from the recording and the profile, and from nothing a broadcaster wrote")]
    public void TheArtefactIsNamedFromTheRecordingAndTheProfileAndNothingElse()
    {
        EncodeFileName artefact = EncodeFileName.Artefact(Recording, Profile);

        Assert.Equal("1872e6a880e94ac6a8f93f740239ef00.0a1b2c3d4e5f406182838485868788a9.mp4", artefact.Value);
        Assert.Equal(artefact, EncodeFileName.Artefact(Recording, Profile));
        Assert.True(artefact.Names(Recording));
        Assert.False(artefact.Names(Job));
    }

    [Fact(DisplayName = "BR-ED2-009: two jobs on one recording with one profile name the same artefact, which is what makes the second a collision")]
    public void TwoJobsOnOneRecordingWithOneProfileNameTheSameArtefact()
        => Assert.Equal(EncodeFileName.Artefact(Recording, Profile), EncodeFileName.Artefact(Recording, Profile));

    [Fact]
    public void TheOnlyWayToMakeANameOutOfTextIsToReadOneBack()
        => Assert.Equal(
            [typeof(string)],
            typeof(EncodeFileName).GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b.mp4")]
    [InlineData("a\\b.mp4")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("a..b.mp4")]
    [InlineData(" a.mp4")]
    [InlineData("a.mp4 ")]
    [InlineData("a\0b.mp4")]
    [InlineData("a\nb.mp4")]
    public void ANameReadBackIsASingleNameAndNeverTheWayOutOfARoom(string value)
        => Assert.ThrowsAny<ArgumentException>(() => new EncodeFileName(value));

    [Fact]
    public void ANameLongerThanAFileSystemAllowsIsNotAName()
        => Assert.Throws<ArgumentException>(() => new EncodeFileName(new string('a', EncodeFileName.MaxLength + 1)));
}
