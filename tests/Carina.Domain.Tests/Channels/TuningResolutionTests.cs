using Carina.Domain.Channels;

namespace Carina.Domain.Tests.Channels;

public sealed class TuningResolutionTests
{
    private static readonly CandidateChannelId Candidate =
        new(Guid.Parse("00000000-0000-0000-0000-00000000002a"));

    public static TheoryData<TuningRefusal> EveryReasonToRefuse =>
        [.. Enum.GetValues<TuningRefusal>().Where(refusal => refusal is not TuningRefusal.None)];

    [Fact]
    public void AResolvedServiceNamesTheCandidateAndWhereItTunes()
    {
        TuningParameters tuning = TuningParameters.Terrestrial(27);

        TuningResolution resolved = TuningResolution.Tunable(Candidate, tuning, impaired: false);

        Assert.True(resolved.CanTune);
        Assert.Equal(TuningRefusal.None, resolved.Refusal);
        Assert.Equal(Candidate, resolved.CandidateChannelId);
        Assert.Equal(tuning, resolved.Tuning);
        Assert.False(resolved.Impaired);
    }

    [Fact]
    public void AResolvedServiceWhoseOnlyTunerIsFaultedSaysSoWithoutRefusing()
    {
        TuningResolution resolved = TuningResolution.Tunable(
            Candidate,
            TuningParameters.Terrestrial(27),
            impaired: true);

        Assert.True(resolved.CanTune);
        Assert.True(resolved.Impaired);
    }

    [Theory]
    [MemberData(nameof(EveryReasonToRefuse))]
    public void ARefusedServiceCannotBeTunedAndNamesNowhere(TuningRefusal refusal)
    {
        TuningResolution refused = TuningResolution.Refused(refusal);

        Assert.False(refused.CanTune);
        Assert.Equal(refusal, refused.Refusal);
        Assert.Null(refused.Tuning);
        Assert.Null(refused.CandidateChannelId);
        Assert.False(refused.Impaired);
    }

    [Fact]
    public void ThereAreFourWaysToRefuseAndTheyAreAllReasons()
    {
        Assert.Equal(4, EveryReasonToRefuse.Count);
    }

    [Fact]
    public void RefusingWithNoReasonIsRefused()
    {
        ArgumentOutOfRangeException thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => TuningResolution.Refused(TuningRefusal.None));

        Assert.Equal("refusal", thrown.ParamName);
    }

    [Fact]
    public void AResolvedServiceWithoutACandidateIsRefused()
    {
        ArgumentNullException thrown = Assert.Throws<ArgumentNullException>(
            () => TuningResolution.Tunable(null!, TuningParameters.Terrestrial(27), impaired: false));

        Assert.Equal("candidateChannelId", thrown.ParamName);
    }

    [Fact]
    public void AResolvedServiceWithNowhereToTuneIsRefused()
    {
        ArgumentNullException thrown = Assert.Throws<ArgumentNullException>(
            () => TuningResolution.Tunable(Candidate, null!, impaired: false));

        Assert.Equal("tuning", thrown.ParamName);
    }
}
