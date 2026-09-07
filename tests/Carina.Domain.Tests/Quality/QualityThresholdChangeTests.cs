using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class QualityThresholdChangeTests
{
    private static readonly DateTime At = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "a change keeps the value it moved from and the value it moved to")]
    public void AChangeKeepsTheValueItMovedFromAndTheValueItMovedTo()
    {
        QualityThresholdChange change = QualityThresholdChange.Record(
            QualityThresholdChangeId.New(),
            QualityThresholdKey.PacketsLostWarning,
            0.0002,
            0.0005,
            At,
            "operator");

        Assert.Equal(QualityThresholdKey.PacketsLostWarning, change.Key);
        Assert.Equal(0.0002, change.PreviousValue);
        Assert.Equal(0.0005, change.NextValue);
        Assert.Equal(At, change.ChangedAt);
        Assert.Equal("operator", change.ChangedBy);
    }

    [Fact]
    public void AChangeNobodySignedIsStillARecordOfTheChange()
        => Assert.Null(QualityThresholdChange
            .Record(QualityThresholdChangeId.New(), QualityThresholdKey.LockRate, 0.99, 0.95, At, null)
            .ChangedBy);

    [Fact]
    public void AChangeUnderAKeyThisDomainDoesNotNameIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(() => QualityThresholdChange.Record(
            QualityThresholdChangeId.New(),
            (QualityThresholdKey)99,
            0,
            1,
            At,
            null));

    [Fact]
    public void AChangeToSomethingThatIsNotANumberIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(() => QualityThresholdChange.Record(
            QualityThresholdChangeId.New(),
            QualityThresholdKey.LockRate,
            0.99,
            double.NaN,
            At,
            null));

    [Fact]
    public void AChangeTimedOutsideUtcIsRefused()
        => Assert.Throws<ArgumentException>(() => QualityThresholdChange.Record(
            QualityThresholdChangeId.New(),
            QualityThresholdKey.LockRate,
            0.99,
            0.95,
            new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Local),
            null));

    [Fact]
    public void ASignatureLongerThanTheColumnHoldsIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(() => QualityThresholdChange.Record(
            QualityThresholdChangeId.New(),
            QualityThresholdKey.LockRate,
            0.99,
            0.95,
            At,
            new string('a', QualityThresholdChange.ChangedByMaxLength + 1)));

    [Fact]
    public void AnEmptyChangeIdIsRefused()
        => Assert.Throws<ArgumentException>(() => new QualityThresholdChangeId(Guid.Empty));
}
