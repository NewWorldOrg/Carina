using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class ExtractionVersionTests
{
    [Fact(DisplayName = "the current version is the version of the calculations, read from a recording's own file")]
    public void TheCurrentVersionIsTheCalculationsReadFromTheFile()
    {
        Assert.Equal(LearningData.ExtractionVersion, ExtractionVersion.Current.Number);
        Assert.Equal(ExtractionOrigin.RecordingFile, ExtractionVersion.Current.Origin);
    }

    [Fact(DisplayName = "data made from a reduced copy is told apart from data made from the file by the same calculations")]
    public void DataFromAReducedCopyIsToldApart()
    {
        var reduced = new ExtractionVersion(LearningData.ExtractionVersion, ExtractionOrigin.ReducedCopy);

        Assert.NotEqual(ExtractionVersion.Current, reduced);
        Assert.Equal(new ExtractionVersion(LearningData.ExtractionVersion, ExtractionOrigin.ReducedCopy), reduced);
        Assert.Equal(1, (int)ExtractionOrigin.RecordingFile);
        Assert.Equal(2, (int)ExtractionOrigin.ReducedCopy);
    }

    [Fact(DisplayName = "the current version made from a reduced copy is the version of the calculations, read from a reduced copy")]
    public void TheCurrentVersionFromAReducedCopyIsTheCalculationsReadFromTheCopy()
    {
        Assert.Equal(
            new ExtractionVersion(LearningData.ExtractionVersion, ExtractionOrigin.ReducedCopy),
            ExtractionVersion.CurrentFromReducedCopy);
    }

    [Fact(DisplayName = "a version is counted from one, and comes from one of the places data is made from")]
    public void AVersionIsCountedFromOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExtractionVersion(0, ExtractionOrigin.RecordingFile));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExtractionVersion(1, (ExtractionOrigin)9));
    }
}
