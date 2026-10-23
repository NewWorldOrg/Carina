using Carina.Domain.Encodings;

namespace Carina.Domain.Tests.Encodings;

public sealed class EncodeRemakeTests
{
    private static EncodeProfile Profile(int rateFactor, int quantiser)
        => EncodeProfile.Define(
            EncodeProfileId.New(),
            new EncodeLabel("Viewing"),
            EncodeCodec.H264,
            EncodeResolution.AsSource,
            Deinterlace.EveryField,
            new ConstantRateFactor(rateFactor),
            new ConstantQuantiser(quantiser),
            new DateTime(2026, 9, 5, 3, 0, 0, DateTimeKind.Utc));

    [Fact(DisplayName = "a job makes its artefact again for one reason, and says once it is done which it kept and whether that is still larger")]
    public void AJobMakesItsArtefactAgainForOneReason()
        => Assert.Equal(
            [EncodeRemake.LargerThanTheSource, EncodeRemake.StillLargerThanTheSource, EncodeRemake.NoSmallerTheSecondTime],
            Enum.GetValues<EncodeRemake>());

    [Fact(DisplayName = "an artefact larger than its recording outgrew it, and one as large or smaller did not")]
    public void AnArtefactLargerThanItsRecordingOutgrewIt()
    {
        Assert.True(EncodeRemakes.Outgrew(made: 1_001, source: 1_000));
        Assert.False(EncodeRemakes.Outgrew(made: 1_000, source: 1_000));
        Assert.False(EncodeRemakes.Outgrew(made: 999, source: 1_000));
    }

    [Fact(DisplayName = "a recording whose size is not known is outgrown by nothing, so the artefact is kept as it was made")]
    public void ARecordingWhoseSizeIsNotKnownIsOutgrownByNothing()
    {
        Assert.False(EncodeRemakes.Outgrew(made: 1_000, source: 0));
        Assert.False(EncodeRemakes.Outgrew(made: 1_000, source: -1));
    }

    [Fact(DisplayName = "a run encodes at the profile's rate factor on the processor and its quantiser on the card")]
    public void ARunEncodesAtTheProfilesRates()
    {
        EncodeRate rate = EncodeRate.Of(Profile(rateFactor: 21, quantiser: 27));

        Assert.Equal(21, rate.On(EncodeEncoder.Software));
        Assert.Equal(27, rate.On(EncodeEncoder.Vaapi));
    }

    [Fact(DisplayName = "the run that makes it again is four coarser on both, and never coarser than the coarsest")]
    public void TheRunThatMakesItAgainIsFourCoarser()
    {
        EncodeRate coarser = EncodeRate.Of(Profile(rateFactor: 21, quantiser: 27)).Coarser();
        EncodeRate topped = EncodeRate.Of(Profile(rateFactor: 49, quantiser: ConstantQuantiser.Coarsest)).Coarser();

        Assert.Equal(25, coarser.On(EncodeEncoder.Software));
        Assert.Equal(31, coarser.On(EncodeEncoder.Vaapi));
        Assert.Equal(ConstantRateFactor.Coarsest, topped.On(EncodeEncoder.Software));
        Assert.Equal(ConstantQuantiser.Coarsest, topped.On(EncodeEncoder.Vaapi));
    }

    [Fact(DisplayName = "a second try smaller than the recording is kept, and is no larger than the recording")]
    public void ASecondTrySmallerThanTheRecordingIsKept()
    {
        Assert.Equal(EncodeRemake.LargerThanTheSource, EncodeRemakes.Judge(source: 1_000, first: 1_200, second: 800));
        Assert.Equal(EncodeRemake.LargerThanTheSource, EncodeRemakes.Judge(source: 1_000, first: 1_200, second: 1_000));
    }

    [Fact(DisplayName = "a second try still larger than the recording is kept when it is smaller than the first")]
    public void ASecondTryStillLargerButSmallerThanTheFirstIsKept()
        => Assert.Equal(EncodeRemake.StillLargerThanTheSource, EncodeRemakes.Judge(source: 1_000, first: 1_200, second: 1_100));

    [Fact(DisplayName = "a second try no smaller than the first leaves the first")]
    public void ASecondTryNoSmallerThanTheFirstLeavesTheFirst()
    {
        Assert.Equal(EncodeRemake.NoSmallerTheSecondTime, EncodeRemakes.Judge(source: 1_000, first: 1_200, second: 1_300));
        Assert.Equal(EncodeRemake.NoSmallerTheSecondTime, EncodeRemakes.Judge(source: 1_000, first: 1_200, second: 1_200));
    }

    [Fact(DisplayName = "a second try that made nothing leaves the first")]
    public void ASecondTryThatMadeNothingLeavesTheFirst()
        => Assert.Equal(EncodeRemake.NoSmallerTheSecondTime, EncodeRemakes.Judge(source: 1_000, first: 1_200, second: null));
}
