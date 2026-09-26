using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Encodings;

public sealed class StationWatermarkTests
{
    private static readonly NetworkId Network = new(32741);

    private static readonly ServiceId Service = new(1040);

    private static readonly DateTime LearnedAt = new(2026, 9, 5, 4, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "a watermark is kept against the service and the recording it was learned from")]
    public void AWatermarkIsKeptAgainstTheServiceAndTheRecordingItWasLearnedFrom()
    {
        RecordingId from = RecordingId.New();
        WatermarkMask mask = WatermarkPictures.Learned();

        StationWatermark kept = StationWatermark.Learn(Network, Service, from, mask, LearnedAt);

        Assert.Equal(Network, kept.NetworkId);
        Assert.Equal(Service, kept.ServiceId);
        Assert.Equal(from, kept.LearnedFrom);
        Assert.Equal(LearnedAt, kept.LearnedAt);
        Assert.Equal(mask.Packed(), kept.Mask.Packed());
    }

    [Fact(DisplayName = "a watermark never judges the recording it was learned from, nor a recording of another service")]
    public void AWatermarkNeverJudgesItsOwnRecordingNorAnotherService()
    {
        RecordingId from = RecordingId.New();
        StationWatermark kept = StationWatermark.Learn(Network, Service, from, WatermarkPictures.Learned(), LearnedAt);

        Assert.False(kept.MayJudge(Network, Service, from));
        Assert.False(kept.MayJudge(Network, new ServiceId(1041), RecordingId.New()));
        Assert.False(kept.MayJudge(new NetworkId(32742), Service, RecordingId.New()));
        Assert.True(kept.MayJudge(Network, Service, RecordingId.New()));
    }

    [Fact(DisplayName = "a pattern read back that is no watermark is refused rather than believed")]
    public void APatternThatIsNoWatermarkIsRefused()
    {
        Assert.Throws<ArgumentException>(() => StationWatermark.Rehydrate(
            Network,
            Service,
            RecordingId.New(),
            LearnedAt,
            new byte[WatermarkMask.PackedBytes]));
    }

    [Fact(DisplayName = "the pattern handed in is copied, so changing it afterwards changes nothing kept")]
    public void ThePatternHandedInIsCopied()
    {
        byte[] pattern = WatermarkPictures.Learned().Packed();
        StationWatermark kept = StationWatermark.Rehydrate(Network, Service, RecordingId.New(), LearnedAt, pattern);

        Array.Clear(pattern);

        Assert.NotEqual(pattern, kept.Pattern);
    }
}
