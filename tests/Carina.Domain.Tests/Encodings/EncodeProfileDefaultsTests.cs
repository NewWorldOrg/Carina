using Carina.Domain.Encodings;
using Carina.Domain.Machines;

namespace Carina.Domain.Tests.Encodings;

public sealed class EncodeProfileDefaultsTests
{
    private static MachineCapabilities Can(CardStanding card, params Faculty[] faculties)
        => MachineCapabilities.Of(card, faculties, string.Empty);

    [Fact(DisplayName = "a new profile keeps the source's size, undoes every field into a picture of its own, and starts at rate factor 20 and quantiser 26")]
    public void ANewProfileStartsAtTheValuesTheProductShipsWith()
    {
        EncodeProfileDefaults defaults = EncodeProfileDefaults.For(Can(CardStanding.Usable, Faculty.EncodeH265OnTheCard));

        Assert.Equal(EncodeResolution.AsSource, defaults.Resolution);
        Assert.Equal(Deinterlace.EveryField, defaults.Deinterlace);
        Assert.Equal(new ConstantRateFactor(20), defaults.SoftwareRateControl);
        Assert.Equal(new ConstantQuantiser(26), defaults.VaapiRateControl);
    }

    [Fact(DisplayName = "a machine that writes H.265 on the card starts a new profile at H.265")]
    public void AMachineThatWritesH265OnTheCardStartsAtH265()
        => Assert.Equal(
            EncodeCodec.H265,
            EncodeProfileDefaults.For(Can(CardStanding.Usable, Faculty.EncodeH264OnTheProcessor, Faculty.EncodeH264OnTheCard, Faculty.EncodeH265OnTheCard)).Codec);

    [Fact(DisplayName = "a machine that writes H.265 on the processor alone starts a new profile at H.265")]
    public void AMachineThatWritesH265OnTheProcessorStartsAtH265()
        => Assert.Equal(
            EncodeCodec.H265,
            EncodeProfileDefaults.For(Can(CardStanding.NodeMissing, Faculty.EncodeH264OnTheProcessor, Faculty.EncodeH265OnTheProcessor)).Codec);

    [Fact(DisplayName = "a machine that writes H.265 nowhere starts a new profile at H.264")]
    public void AMachineThatWritesH265NowhereStartsAtH264()
    {
        Assert.Equal(
            EncodeCodec.H264,
            EncodeProfileDefaults.For(Can(CardStanding.Usable, Faculty.EncodeH264OnTheProcessor, Faculty.EncodeH264OnTheCard)).Codec);
        Assert.Equal(EncodeCodec.H264, EncodeProfileDefaults.For(Can(CardStanding.NodeMissing)).Codec);
    }

    [Fact(DisplayName = "the rate factor and quantiser a new profile starts at are the same whichever the codec")]
    public void TheRatesAreTheSameWhicheverTheCodec()
    {
        EncodeProfileDefaults h265 = EncodeProfileDefaults.For(Can(CardStanding.Usable, Faculty.EncodeH265OnTheCard));
        EncodeProfileDefaults h264 = EncodeProfileDefaults.For(Can(CardStanding.NodeMissing, Faculty.EncodeH264OnTheProcessor));

        Assert.Equal(h265.SoftwareRateControl, h264.SoftwareRateControl);
        Assert.Equal(h265.VaapiRateControl, h264.VaapiRateControl);
        Assert.Equal(h265.Resolution, h264.Resolution);
        Assert.Equal(h265.Deinterlace, h264.Deinterlace);
    }
}
