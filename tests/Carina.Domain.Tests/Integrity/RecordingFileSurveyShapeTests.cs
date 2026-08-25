using Carina.Domain.Integrity;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Integrity;

public sealed class RecordingFileSurveyShapeTests
{
    private interface ICouldChangeAFile
    {
        Task<RootListing> ListAsync(OutputRoot root, CancellationToken cancellationToken);

        Task DeleteAsync(OutputRoot root, string fileName, CancellationToken cancellationToken);
    }

    private interface ICouldOnlyLook
    {
        Task<RootListing> ListAsync(OutputRoot root, CancellationToken cancellationToken);
    }

    [Fact]
    public void TheSurveyOffersTheseTwoWaysToLookAndNothingElse()
    {
        Assert.Equal(["ListAsync", "RootsAsync"], ReadOnlyContract.Names(typeof(IRecordingFileSurvey)));
    }

    [Fact]
    public void NothingOnTheSurveyCouldChangeAFile()
    {
        Assert.Empty(ReadOnlyContract.MembersThatCouldChangeAFile(typeof(IRecordingFileSurvey)));
    }

    [Fact]
    public void TheLedgerReaderOffersThisOneWayToLookAndNothingElse()
    {
        Assert.Equal(["ListAsync"], ReadOnlyContract.Names(typeof(IRecordingLedger)));
    }

    [Fact]
    public void NothingOnTheLedgerReaderCouldChangeAFile()
    {
        Assert.Empty(ReadOnlyContract.MembersThatCouldChangeAFile(typeof(IRecordingLedger)));
    }

    [Fact]
    public void TheRuleSeesAWayToChangeAFileWhenThereIsOne()
    {
        Assert.Equal(["DeleteAsync"], ReadOnlyContract.MembersThatCouldChangeAFile(typeof(ICouldChangeAFile)));
    }

    [Fact]
    public void TheRuleSeesNoWayToChangeAFileWhenThereIsNone()
    {
        Assert.Empty(ReadOnlyContract.MembersThatCouldChangeAFile(typeof(ICouldOnlyLook)));
    }
}
