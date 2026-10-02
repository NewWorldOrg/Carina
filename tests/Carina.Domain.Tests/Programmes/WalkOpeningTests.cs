using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Domain.Tests.Programmes;

public sealed class WalkOpeningTests
{
    private static readonly DateTime At = new(2026, 10, 3, 3, 0, 0, DateTimeKind.Utc);

    private static readonly CollectionSettings Settings = new();

    [Fact]
    public void AStreamNeverVisitedIsWaitingForAWalk()
        => Assert.Equal(
            [TuneSystem.IsdbT],
            WalkOpening.Awaited([Terrestrial(1)], [], At, Settings));

    [Fact]
    public void AStreamStillInsideItsWaitIsNotWaitingForAWalk()
        => Assert.Empty(WalkOpening.Awaited(
            [Terrestrial(1)],
            [Visited(1, VisitOutcome.Complete, At.AddHours(-1))],
            At,
            Settings));

    [Fact]
    public void AStreamTurnedAwayOnTheLastWalkIsWaitingForTheNext()
        => Assert.Equal(
            [TuneSystem.IsdbT],
            WalkOpening.Awaited(
                [Terrestrial(1), Terrestrial(2)],
                [
                    Visited(1, VisitOutcome.Interrupted, At.AddMinutes(-10)),
                    Visited(2, VisitOutcome.Complete, At.AddHours(-1)),
                ],
                At,
                Settings));

    [Fact]
    public void EachKindOfBroadcastWaitingIsNamedOnce()
        => Assert.Equal(
            [TuneSystem.IsdbT, TuneSystem.IsdbSBs],
            WalkOpening.Awaited([Terrestrial(1), Terrestrial(2), Satellite(3)], [], At, Settings));

    [Fact]
    public void AnIdleTunerThatReceivesWhatIsWaitingIsAnOpening()
        => Assert.True(WalkOpening.IsThere(
            [TuneSystem.IsdbT],
            [Tuner("a", TunerKind.Terrestrial, TunerState.Busy), Tuner("b", TunerKind.Terrestrial, TunerState.Idle)]));

    [Fact]
    public void ATunerInUseIsNoOpening()
        => Assert.False(WalkOpening.IsThere(
            [TuneSystem.IsdbT],
            [Tuner("a", TunerKind.Terrestrial, TunerState.Busy)]));

    [Fact]
    public void AnIdleTunerOfAKindThatCannotReceiveWhatIsWaitingIsNoOpening()
        => Assert.False(WalkOpening.IsThere(
            [TuneSystem.IsdbT],
            [Tuner("a", TunerKind.Terrestrial, TunerState.Busy), Tuner("s", TunerKind.Satellite, TunerState.Idle)]));

    [Theory]
    [InlineData(TunerState.Disabled)]
    [InlineData(TunerState.Faulted)]
    [InlineData(TunerState.Draining)]
    [InlineData(TunerState.Unspecified)]
    public void ATunerOutOfServiceIsNoOpening(TunerState state)
        => Assert.False(WalkOpening.IsThere([TuneSystem.IsdbT], [Tuner("a", TunerKind.Terrestrial, state)]));

    [Fact]
    public void NothingWaitingIsNoOpeningHoweverManyTunersAreIdle()
        => Assert.False(WalkOpening.IsThere([], [Tuner("a", TunerKind.Terrestrial, TunerState.Idle)]));

    private static BroadcastStream Terrestrial(int stream)
        => new(new NetworkId(9), new TransportStreamId(stream), TuningParameters.Terrestrial(20 + stream), []);

    private static BroadcastStream Satellite(int stream)
        => new(new NetworkId(9), new TransportStreamId(stream), TuningParameters.Bs(1, new TransportStreamId(stream)), []);

    private static StreamVisit Visited(int stream, VisitOutcome outcome, DateTime at)
        => StreamVisit.Record(new NetworkId(9), new TransportStreamId(stream), outcome, at, TimeSpan.FromSeconds(1));

    private static TunerSnapshot Tuner(string deviceId, TunerKind kind, TunerState state)
        => new(deviceId, kind, state);
}
