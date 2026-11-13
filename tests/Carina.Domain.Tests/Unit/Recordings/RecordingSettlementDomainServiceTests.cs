using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Unit.Recordings;

[Trait("Category", "Unit")]
public sealed class RecordingSettlementDomainServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Now = RecordingFactory.Now;

    [Fact(DisplayName = "BR-KD-002: a recording is weighed against the rate measured off the kind of channel its service is selected on")]
    public async Task ARecordingIsWeighedAgainstTheRateOfItsChannel()
    {
        SettlementRate terrestrial = await Settlement(TuningResolution.Tunable(
                CandidateChannelId.New(),
                TuningParameters.Terrestrial(27),
                false))
            .RateAsync(RecordingFactory.Started(), Cancel);
        SettlementRate satellite = await Settlement(TuningResolution.Refused(
                TuningRefusal.NoTunerForSystem,
                TuningParameters.Bs(1, new TransportStreamId(16625))))
            .RateAsync(RecordingFactory.Started(), Cancel);

        Assert.Equal(ExpectedBitrate.Terrestrial, terrestrial.Bitrate);
        Assert.False(terrestrial.OfEveryKind);
        Assert.Equal(ExpectedBitrate.Satellite, satellite.Bitrate);
        Assert.False(satellite.OfEveryKind);
    }

    [Theory(DisplayName = "BR-KD-002: a recording whose service or selected channel is gone is weighed against the range every kind of broadcast falls in")]
    [InlineData(TuningRefusal.NoSuchService)]
    [InlineData(TuningRefusal.NoSelectedChannel)]
    public async Task ARecordingWhoseChannelIsGoneIsWeighedAgainstEveryKind(TuningRefusal refusal)
    {
        SettlementRate rate = await Settlement(TuningResolution.Refused(refusal))
            .RateAsync(RecordingFactory.Started(), Cancel);

        Assert.Equal(ExpectedBitrate.OfAnyKind, rate.Bitrate);
        Assert.True(rate.OfEveryKind);
        Assert.Equal(refusal, rate.Refusal);
    }

    [Theory(DisplayName = "BR-KD-002: a recording whose channel the catalogue cannot name for any other reason is not weighed yet")]
    [InlineData(TuningRefusal.CapacityUnknown)]
    [InlineData(TuningRefusal.LedgerUnreadable)]
    [InlineData(TuningRefusal.NoTunerForSystem)]
    public async Task ARecordingWhoseChannelCannotBeNamedIsNotWeighedYet(TuningRefusal refusal)
    {
        SettlementRate rate = await Settlement(TuningResolution.Refused(refusal))
            .RateAsync(RecordingFactory.Started(), Cancel);

        Assert.Null(rate.Bitrate);
        Assert.False(rate.OfEveryKind);
        Assert.Equal(refusal, rate.Refusal);
    }

    [Fact(DisplayName = "BR-KD-023: a recording is judged against the quality levels in force at the moment it ends")]
    public async Task ARecordingIsJudgedAgainstTheLevelsInForce()
    {
        QualityBands read = await Settlement(TuningResolution.Refused(TuningRefusal.NoSuchService)).BandsAsync(Now, Cancel);
        QualityBands expected = QualityThresholdStanding.Bands(QualityThresholdStanding.Over([], Now));

        Assert.All(QualityMetrics.All, metric => Assert.Equal(expected.For(metric), read.For(metric)));
    }

    private static RecordingSettlementDomainService Settlement(TuningResolution resolution)
        => new(new ResolvedTuning(resolution), new HeldQualityThresholds());
}
