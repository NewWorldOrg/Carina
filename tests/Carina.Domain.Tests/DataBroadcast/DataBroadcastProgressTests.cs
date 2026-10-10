using Carina.Domain.DataBroadcast;

namespace Carina.Domain.Tests.DataBroadcast;

public sealed class DataBroadcastProgressTests
{
    [Fact(DisplayName = "BR-BS-001: a recording still being written has no record due")]
    public void ARecordingStillBeingWrittenHasNoRecordDue()
    {
        Assert.Equal((DataBroadcastState.None, 0, (int?)null), Of(DataBroadcastProgress.NotYet));
    }

    [Fact(DisplayName = "BR-BS-001: a recording that ends has its record coming")]
    public void ARecordingThatEndsHasItsRecordComing()
    {
        Assert.Equal(DataBroadcastState.Coming, DataBroadcastProgress.NotYet.RecordingEnded().State);
    }

    [Fact(DisplayName = "BR-BS-001: a record taken with modules in it is made and says how many")]
    public void ARecordTakenWithModulesIsMade()
    {
        Assert.Equal((DataBroadcastState.Made, 0, (int?)36), Of(DataBroadcastProgress.NotYet.RecordingEnded().Taken(36)));
    }

    [Fact(DisplayName = "BR-BS-001: a record taken from a recording with no data broadcast is missing")]
    public void ARecordTakenWithNothingInItIsMissing()
    {
        Assert.Equal((DataBroadcastState.Missing, 0, (int?)null), Of(DataBroadcastProgress.NotYet.RecordingEnded().Taken(0)));
    }

    [Fact(DisplayName = "BR-BS-001: a record is tried three times in all and then stays failed, as captions are")]
    public void ARecordIsTriedThreeTimesInAllAndThenStaysFailed()
    {
        DataBroadcastProgress progress = DataBroadcastProgress.NotYet.RecordingEnded().Failed();
        int retries = 0;

        while (progress.IsRetryDue)
        {
            progress = progress.Retried().Failed();
            retries++;
        }

        Assert.Equal(2, retries);
        Assert.Equal((DataBroadcastState.Failed, DataBroadcastProgress.TriesAtMost, (int?)null), Of(progress));
        Assert.Throws<InvalidOperationException>(() => progress.Retried());
    }

    [Fact(DisplayName = "BR-BS-001: a record tried again keeps its failures until it is made")]
    public void ARecordTriedAgainKeepsItsFailuresUntilItIsMade()
    {
        DataBroadcastProgress retried = DataBroadcastProgress.NotYet.RecordingEnded().Failed().Retried();

        Assert.Equal((DataBroadcastState.Coming, 1, (int?)null), Of(retried));
        Assert.Equal((DataBroadcastState.Made, 0, (int?)2), Of(retried.Taken(2)));
    }

    [Fact]
    public void OnlyARecordThatIsComingIsTakenOrFails()
    {
        Assert.Throws<InvalidOperationException>(() => DataBroadcastProgress.NotYet.Taken(1));
        Assert.Throws<InvalidOperationException>(() => DataBroadcastProgress.NotYet.Failed());
        Assert.Throws<InvalidOperationException>(() => DataBroadcastProgress.NotYet.RecordingEnded().Taken(1).Failed());
        Assert.Throws<InvalidOperationException>(() => DataBroadcastProgress.NotYet.RecordingEnded().RecordingEnded());
        Assert.Throws<InvalidOperationException>(() => DataBroadcastProgress.NotYet.Retried());
        Assert.Throws<InvalidOperationException>(() => DataBroadcastProgress.NotYet.RecordingEnded().Taken(1).RecordingEnded());
    }

    [Theory(DisplayName = "BR-BS-001: a record already taken is taken again once its recording is descrambled")]
    [InlineData(1)]
    [InlineData(0)]
    public void ARecordAlreadyTakenIsTakenAgainOnceDescrambled(int modules)
    {
        DataBroadcastProgress taken = DataBroadcastProgress.NotYet.RecordingEnded().Taken(modules);

        Assert.Equal((DataBroadcastState.Coming, 0, (int?)null), Of(taken.Descrambled()));
    }

    [Fact(DisplayName = "BR-BS-001: a record that failed every try is tried three times more once its recording is descrambled")]
    public void ARecordThatFailedEveryTryIsTriedThreeTimesMoreOnceDescrambled()
    {
        DataBroadcastProgress failed = DataBroadcastProgress.NotYet.RecordingEnded().Failed().Retried().Failed().Retried().Failed();

        DataBroadcastProgress again = failed.Descrambled();

        Assert.Equal((DataBroadcastState.Coming, 0, (int?)null), Of(again));
        Assert.True(again.Failed().IsRetryDue);
        Assert.False(again.Failed().Retried().Failed().Retried().Failed().IsRetryDue);
    }

    [Fact(DisplayName = "BR-BS-001: a record that is made and no longer kept comes again with no failures counted")]
    public void ARecordMadeAndNoLongerKeptComesAgain()
    {
        DataBroadcastProgress made = DataBroadcastProgress.NotYet.RecordingEnded().Failed().Retried().Taken(4);

        Assert.Equal((DataBroadcastState.Coming, 0, (int?)null), Of(made.Lost()));
    }

    [Fact]
    public void OnlyARecordThatIsMadeIsLost()
    {
        Assert.Throws<InvalidOperationException>(() => DataBroadcastProgress.NotYet.Lost());
        Assert.Throws<InvalidOperationException>(() => DataBroadcastProgress.NotYet.RecordingEnded().Lost());
        Assert.Throws<InvalidOperationException>(() => DataBroadcastProgress.NotYet.RecordingEnded().Taken(0).Lost());
        Assert.Throws<InvalidOperationException>(() => DataBroadcastProgress.NotYet.RecordingEnded().Failed().Lost());
    }

    [Fact]
    public void ARecordNotYetTakenIsNotTakenAgain()
    {
        Assert.Throws<InvalidOperationException>(() => DataBroadcastProgress.NotYet.Descrambled());
        Assert.Throws<InvalidOperationException>(() => DataBroadcastProgress.NotYet.RecordingEnded().Descrambled());
    }

    [Theory]
    [InlineData(DataBroadcastState.Made, 0, null)]
    [InlineData(DataBroadcastState.Missing, 0, 3)]
    [InlineData(DataBroadcastState.Failed, 0, null)]
    [InlineData(DataBroadcastState.Made, 1, 3)]
    [InlineData(DataBroadcastState.None, 1, null)]
    [InlineData(DataBroadcastState.Coming, -1, null)]
    [InlineData((DataBroadcastState)0, 0, null)]
    public void AProgressThatDoesNotAddUpIsRefused(DataBroadcastState state, int attempts, int? modules)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DataBroadcastProgress(state, attempts, modules));
    }

    [Fact]
    public void TheStatesAreTheFiveTheRowHolds()
    {
        Assert.Equal(
            ["None", "Coming", "Made", "Missing", "Failed"],
            Enum.GetValues<DataBroadcastState>().Select(state => state.ToString()));
    }

    private static (DataBroadcastState, int, int?) Of(DataBroadcastProgress progress)
        => (progress.State, progress.Attempts, progress.Modules);

    [Theory(DisplayName = "BR-BS-001: a record is ready to play only while it is made and kept, coming while it is taken or tried again, and none otherwise")]
    [InlineData(DataBroadcastState.None, 0, null, true, DataBroadcastStanding.None)]
    [InlineData(DataBroadcastState.Coming, 0, null, false, DataBroadcastStanding.Coming)]
    [InlineData(DataBroadcastState.Coming, 2, null, false, DataBroadcastStanding.Coming)]
    [InlineData(DataBroadcastState.Made, 0, 3, true, DataBroadcastStanding.Ready)]
    [InlineData(DataBroadcastState.Made, 0, 3, false, DataBroadcastStanding.Coming)]
    [InlineData(DataBroadcastState.Missing, 0, null, true, DataBroadcastStanding.None)]
    [InlineData(DataBroadcastState.Failed, 1, null, false, DataBroadcastStanding.Coming)]
    [InlineData(DataBroadcastState.Failed, 2, null, false, DataBroadcastStanding.Coming)]
    [InlineData(DataBroadcastState.Failed, 3, null, true, DataBroadcastStanding.None)]
    public void ARecordIsReadyOnlyWhileMadeAndKept(DataBroadcastState state, int attempts, int? modules, bool kept, DataBroadcastStanding standing)
    {
        Assert.Equal(standing, new DataBroadcastProgress(state, attempts, modules).StandingWith(kept));
    }
}
