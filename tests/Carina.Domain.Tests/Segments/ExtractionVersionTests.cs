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

    [Fact(DisplayName = "data whose time zero is where the file begins and whose picture was decoded at half its size is the second version, apart from what the first one made")]
    public void DataFromWhereTheFileBeginsIsTheSecondVersion()
    {
        Assert.Equal(2, ExtractionVersion.Current.Number);
        Assert.NotEqual(new ExtractionVersion(1, ExtractionOrigin.RecordingFile), ExtractionVersion.Current);
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

    [Fact(DisplayName = "a version is counted from one, and comes from one of the places data is made from")]
    public void AVersionIsCountedFromOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExtractionVersion(0, ExtractionOrigin.RecordingFile));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExtractionVersion(1, (ExtractionOrigin)9));
    }
}
