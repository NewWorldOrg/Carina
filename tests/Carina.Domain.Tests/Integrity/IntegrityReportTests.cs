using Carina.Domain.Integrity;
using Carina.Domain.Recordings;

using static Carina.Domain.Tests.Integrity.IntegrityFixtures;

namespace Carina.Domain.Tests.Integrity;

public sealed class IntegrityReportTests
{
    private static readonly IntegrityCheckId Other =
        new(new Guid("9f2b7c10-0000-0000-0000-000000000002"));

    private static IntegrityCheck Checked(IntegrityCheckId id)
        => IntegrityCheck.Rehydrate(id, At, Done, 0, 0, 0, 0, 0, 0, 0);

    private static IntegrityFinding Found(IntegrityCheckId id)
        => IntegrityFinding.NoLedgerRow(id, Primary, "stray.m2ts", 1, At);

    [Fact]
    public void AReportHoldsTheCheckAndWhatItFound()
    {
        IntegrityReport report = IntegrityReport.Of(Checked(Check), [Found(Check)]);

        Assert.Equal(Check, report.Check.Id);
        Assert.Single(report.Findings);
    }

    [Fact]
    public void AReportOfACleanCheckHoldsNoFindings()
    {
        Assert.Empty(IntegrityReport.Of(Checked(Check), []).Findings);
    }

    [Fact]
    public void AFindingFromAnotherCheckIsRefused()
    {
        Assert.Throws<ArgumentException>(() => IntegrityReport.Of(Checked(Check), [Found(Other)]));
    }

    [Fact]
    public void AReportWithNoCheckIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => IntegrityReport.Of(null!, []));
    }

    [Fact]
    public void AReportWithNoListOfFindingsIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => IntegrityReport.Of(Checked(Check), null!));
    }

    [Fact]
    public void AHoleAmongTheFindingsIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => IntegrityReport.Of(Checked(Check), [null!]));
    }

    [Fact]
    public void AReportKeepsItsOwnCopyOfWhatWasFound()
    {
        List<IntegrityFinding> findings = [Found(Check)];
        IntegrityReport report = IntegrityReport.Of(Checked(Check), findings);

        findings.Clear();

        Assert.Single(report.Findings);
    }
}
