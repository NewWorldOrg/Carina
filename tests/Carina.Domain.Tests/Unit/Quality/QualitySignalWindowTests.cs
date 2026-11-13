using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

using Puppis.Contracts;

namespace Carina.Domain.Tests.Unit.Quality;

[Trait("Category", "Unit")]
public sealed class QualitySignalWindowTests
{
    private static readonly DateTime Noon = new(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Between = TimeSpan.FromSeconds(10);

    [Fact(DisplayName = "figures taken at different moments are last taken at the later of the two")]
    public void FiguresTakenAtDifferentMomentsAreLastTakenAtTheLaterOfTheTwo()
    {
        QualitySignalSample sample = QualitySignalSample.Rehydrate(
            "instance-a",
            SessionId.Parse("live-1"),
            Noon + (Between * 3),
            SessionPurpose.Live,
            new TunerDeviceId("adapter3.frontend0"),
            new NetworkId(32736),
            new ServiceId(1024),
            SignalSample.WithLock(
                Noon + (Between * 3),
                30_000,
                Noon,
                [new LayerBitErrorCounts(0, 8, 4_000_000)],
                Noon + Between));

        Assert.Equal(Noon + Between, QualitySignalWindow.Of(sample).LastCarriedAt);
    }
}
