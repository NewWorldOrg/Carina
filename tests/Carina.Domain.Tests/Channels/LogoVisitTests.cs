using Carina.Domain.Channels;

namespace Carina.Domain.Tests.Channels;

public sealed class LogoVisitTests
{
    [Theory(DisplayName = "BR-TD-008: a visit that was not interrupted collected when a picture arrived, and found nothing otherwise")]
    [InlineData(false, 2, LogoVisitOutcome.Collected)]
    [InlineData(false, 1, LogoVisitOutcome.Collected)]
    [InlineData(false, 0, LogoVisitOutcome.NothingArrived)]
    [InlineData(true, 2, LogoVisitOutcome.Interrupted)]
    [InlineData(true, 0, LogoVisitOutcome.Interrupted)]
    public void AVisitEndsAsWhatArrived(bool interrupted, int pictures, LogoVisitOutcome outcome)
        => Assert.Equal(outcome, LogoVisit.OutcomeOf(interrupted, pictures));
}
