using Carina.Domain.Integrity;

using static Carina.Domain.Tests.Integrity.IntegrityFixtures;

namespace Carina.Domain.Tests.Integrity;

public sealed class IntegritySweepTests
{
    private static IntegritySweep Of(
        int rootsWalked = 0,
        int rootsOutOfReach = 0,
        int filesRead = 0,
        int ledgerRowsRead = 0,
        int ledgerRowsJudged = 0,
        int ledgerRowsStillWriting = 0,
        int ledgerRowsInRootsOutOfReach = 0,
        IReadOnlyList<IntegrityFinding>? findings = null)
        => IntegritySweep.Of(
            At,
            rootsWalked,
            rootsOutOfReach,
            filesRead,
            ledgerRowsRead,
            ledgerRowsJudged,
            ledgerRowsStillWriting,
            ledgerRowsInRootsOutOfReach,
            findings ?? []);

    [Fact]
    public void ASweepKeepsEveryCountItWasHanded()
    {
        IntegritySweep swept = Of(1, 2, 3, 4, 5, 6, 7);

        Assert.Equal(1, swept.RootsWalked);
        Assert.Equal(2, swept.RootsOutOfReach);
        Assert.Equal(3, swept.FilesRead);
        Assert.Equal(4, swept.LedgerRowsRead);
        Assert.Equal(5, swept.LedgerRowsJudged);
        Assert.Equal(6, swept.LedgerRowsStillWriting);
        Assert.Equal(7, swept.LedgerRowsInRootsOutOfReach);
        Assert.Equal(At, swept.RanAt);
    }

    [Theory]
    [InlineData(-1, 0, 0, 0, 0, 0, 0)]
    [InlineData(0, -1, 0, 0, 0, 0, 0)]
    [InlineData(0, 0, -1, 0, 0, 0, 0)]
    [InlineData(0, 0, 0, -1, 0, 0, 0)]
    [InlineData(0, 0, 0, 0, -1, 0, 0)]
    [InlineData(0, 0, 0, 0, 0, -1, 0)]
    [InlineData(0, 0, 0, 0, 0, 0, -1)]
    public void ASweepCountsNothingNegative(
        int rootsWalked,
        int rootsOutOfReach,
        int filesRead,
        int ledgerRowsRead,
        int ledgerRowsJudged,
        int ledgerRowsStillWriting,
        int ledgerRowsInRootsOutOfReach)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Of(
                rootsWalked,
                rootsOutOfReach,
                filesRead,
                ledgerRowsRead,
                ledgerRowsJudged,
                ledgerRowsStillWriting,
                ledgerRowsInRootsOutOfReach));
    }

    [Fact]
    public void ASweepWithNoListOfFindingsIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => IntegritySweep.Of(At, 0, 0, 0, 0, 0, 0, 0, null!));
    }

    [Fact]
    public void ATimeThatIsNotInUtcIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => IntegritySweep.Of(
                new DateTime(2026, 8, 26, 3, 0, 0, DateTimeKind.Local),
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                []));
    }

    [Fact]
    public void ASweepKeepsItsOwnCopyOfWhatItFound()
    {
        List<IntegrityFinding> findings = [IntegrityFinding.NoLedgerRow(Primary, "stray.m2ts", 1, At)];
        IntegritySweep swept = Of(findings: findings);

        findings.Clear();

        Assert.Single(swept.Findings);
    }
}
