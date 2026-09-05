using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class QualitySubjectTests
{
    [Fact(DisplayName = "BR-QD-013: a subject is named by value, whichever of the four it is")]
    public void ASubjectIsNamedByValueWhicheverOfTheFourItIs()
    {
        QualitySubject subject = QualitySubject.Of(QualitySubjectKind.Channel, "32736-1024");

        Assert.Equal(QualitySubjectKind.Channel, subject.Kind);
        Assert.Equal("32736-1024", subject.Key);
    }

    [Fact]
    public void ASubjectNobodyNamedIsNoSubject()
        => Assert.Throws<ArgumentException>(() => QualitySubject.Of(QualitySubjectKind.Tuner, "  "));

    [Fact]
    public void ASubjectNamedAtGreatLengthIsRefusedRatherThanCutShort()
        => Assert.Throws<ArgumentException>(
            () => QualitySubject.Of(QualitySubjectKind.Tuner, new string('a', QualitySubject.KeyMaxLength + 1)));

    [Fact]
    public void AKindThisDomainDoesNotWatchIsNoKind()
        => Assert.Throws<ArgumentOutOfRangeException>(() => QualitySubject.Of((QualitySubjectKind)9, "adapter0"));
}
