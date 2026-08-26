using Carina.Domain.Integrity;

using static Carina.Domain.Tests.Integrity.IntegrityFixtures;

namespace Carina.Domain.Tests.Integrity;

public sealed class IntegrityCheckTests
{
    private static IntegrityCheck Of(
        int rootsWalked = 0,
        int rootsOutOfReach = 0,
        int filesRead = 0,
        int ledgerRowsRead = 0,
        int ledgerRowsJudged = 0,
        int ledgerRowsStillWriting = 0,
        int ledgerRowsInRootsOutOfReach = 0)
        => IntegrityCheck.Rehydrate(
            Check,
            At,
            Done,
            rootsWalked,
            rootsOutOfReach,
            filesRead,
            ledgerRowsRead,
            ledgerRowsJudged,
            ledgerRowsStillWriting,
            ledgerRowsInRootsOutOfReach);

    [Fact]
    public void ACheckKeepsEveryCountItWasHanded()
    {
        IntegrityCheck check = Of(1, 2, 3, 4, 5, 6, 7);

        Assert.Equal(1, check.RootsWalked);
        Assert.Equal(2, check.RootsOutOfReach);
        Assert.Equal(3, check.FilesRead);
        Assert.Equal(4, check.LedgerRowsRead);
        Assert.Equal(5, check.LedgerRowsJudged);
        Assert.Equal(6, check.LedgerRowsStillWriting);
        Assert.Equal(7, check.LedgerRowsInRootsOutOfReach);
        Assert.Equal(At, check.StartedAt);
        Assert.Equal(Done, check.FinishedAt);
        Assert.Equal(Check, check.Id);
    }

    [Theory]
    [InlineData(-1, 0, 0, 0, 0, 0, 0)]
    [InlineData(0, -1, 0, 0, 0, 0, 0)]
    [InlineData(0, 0, -1, 0, 0, 0, 0)]
    [InlineData(0, 0, 0, -1, 0, 0, 0)]
    [InlineData(0, 0, 0, 0, -1, 0, 0)]
    [InlineData(0, 0, 0, 0, 0, -1, 0)]
    [InlineData(0, 0, 0, 0, 0, 0, -1)]
    public void ACheckCountsNothingNegative(
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
    public void ACheckWithNoIdIsRefused()
    {
        Assert.Throws<ArgumentNullException>(
            () => IntegrityCheck.Rehydrate(null!, At, Done, 0, 0, 0, 0, 0, 0, 0));
    }

    [Fact]
    public void ACheckThatFinishesBeforeItStartsIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => IntegrityCheck.Rehydrate(Check, Done, At, 0, 0, 0, 0, 0, 0, 0));
    }

    [Fact]
    public void ACheckThatStartsAndFinishesInTheSameMomentIsAllowed()
    {
        Assert.Equal(At, IntegrityCheck.Rehydrate(Check, At, At, 0, 0, 0, 0, 0, 0, 0).FinishedAt);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void AStartThatIsNotInUtcIsRefused(DateTimeKind kind)
    {
        Assert.Throws<ArgumentException>(
            () => IntegrityCheck.Rehydrate(
                Check,
                new DateTime(2026, 8, 26, 3, 0, 0, kind),
                Done,
                0,
                0,
                0,
                0,
                0,
                0,
                0));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void AnEndThatIsNotInUtcIsRefused(DateTimeKind kind)
    {
        Assert.Throws<ArgumentException>(
            () => IntegrityCheck.Rehydrate(
                Check,
                At,
                new DateTime(2026, 8, 26, 3, 0, 2, kind),
                0,
                0,
                0,
                0,
                0,
                0,
                0));
    }
}
