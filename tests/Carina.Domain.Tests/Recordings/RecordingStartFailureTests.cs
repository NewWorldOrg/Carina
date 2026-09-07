using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingStartFailureTests
{
    public static TheoryData<TuneFailureKind, bool> WhichOfTheFourTheTunerHearsAbout =>
        new()
        {
            { TuneFailureKind.NoLock, true },
            { TuneFailureKind.NoData, true },
            { TuneFailureKind.IncompletePsi, false },
            { TuneFailureKind.StreamMismatch, false },
        };

    public static TheoryData<TuneFailureKind> AllFourOfThem =>
        [TuneFailureKind.NoLock, TuneFailureKind.NoData, TuneFailureKind.IncompletePsi, TuneFailureKind.StreamMismatch];

    [Theory]
    [MemberData(nameof(WhichOfTheFourTheTunerHearsAbout))]
    public void OnlyTheTwoFailuresThatSayReceptionIsBadReachTheRotation(TuneFailureKind kind, bool expected)
        => Assert.Equal(expected, RecordingStartFailure.TheTunerWouldNotTune(kind).IsWorthReportingToTheTuner);

    [Fact]
    public void LosingAContestForATunerIsNeverReportedAsBadReception()
        => Assert.False(RecordingStartFailure.NoTunerWasFree.IsWorthReportingToTheTuner);

    [Theory]
    [MemberData(nameof(AllFourOfThem))]
    public void EveryOneOfTheFourReachesTheLedgerAsATuneFailure(TuneFailureKind kind)
    {
        RecordingStartFailure failure = RecordingStartFailure.TheTunerWouldNotTune(kind);

        Assert.Equal(ReservationOutcomeKind.TuneFailure, failure.Kind);
        Assert.Equal(RecordingFault.TuneFailed, failure.Fault);
        Assert.Equal(kind, failure.TuneFailure);
    }

    [Fact]
    public void AContestForATunerReachesTheLedgerAsACompetingReservationThatNamesNoTuneFailure()
    {
        Assert.Equal(ReservationOutcomeKind.Competing, RecordingStartFailure.NoTunerWasFree.Kind);
        Assert.Equal(RecordingFault.TunerContended, RecordingStartFailure.NoTunerWasFree.Fault);
        Assert.Null(RecordingStartFailure.NoTunerWasFree.TuneFailure);
    }

    [Fact]
    public void AFailureThatIsNotOneOfTheFourIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => RecordingStartFailure.TheTunerWouldNotTune((TuneFailureKind)9));
}
