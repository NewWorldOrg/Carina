using Carina.Domain.Machines;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class LiveEncoderSelectionTests
{
    [Fact]
    public async Task TheCardIsUsedWhenThisMachineCanEncodeOnIt()
    {
        LiveEncoderChoice chosen = await Choosing(LiveEncoder.Vaapi, WithACard);

        Assert.Equal(LiveEncoder.Vaapi, chosen.Encoder);
        Assert.False(chosen.FellBack);
    }

    [Theory]
    [InlineData(CardStanding.NodeMissing)]
    [InlineData(CardStanding.NodeUnreadable)]
    [InlineData(CardStanding.DriverUnusable)]
    [InlineData(CardStanding.ProbeTimedOut)]
    [InlineData(CardStanding.ProbeProgrammeMissing)]
    public async Task ACardThisMachineCannotEncodeOnMeansSoftwareAndSaysWhy(CardStanding standing)
    {
        LiveEncoderChoice chosen = await Choosing(
            LiveEncoder.Vaapi,
            MachineCapabilities.Of(standing, [], "the card was turned down"));

        Assert.Equal(LiveEncoder.Software, chosen.Encoder);
        Assert.Equal(standing, chosen.FellBackBecause);
        Assert.Equal("the card was turned down", chosen.Note);
    }

    [Fact]
    public async Task SoftwareIsWhatIsAskedForUntilSomebodyAsksForTheCard()
    {
        var machine = new WhatIsAsked(WithACard);

        LiveEncoderChoice chosen = await new LiveEncoderSelection(new LiveTranscodeSettings(), machine)
            .ChooseAsync(CancellationToken.None);

        Assert.Equal(LiveEncoder.Software, chosen.Encoder);
        Assert.False(chosen.FellBack);
        Assert.Equal(0, machine.Times);
    }

    [Fact]
    public async Task LiveAsksTheMachineRatherThanWorkingItOutItself()
    {
        var machine = new WhatIsAsked(WithACard);

        await new LiveEncoderSelection(new LiveTranscodeSettings { Prefer = LiveEncoder.Vaapi }, machine)
            .ChooseAsync(CancellationToken.None);

        Assert.Equal(1, machine.Times);
    }

    [Fact(DisplayName = "a card that encodes all three formats offers all three, H.264 first")]
    public async Task ACardThatEncodesAllThreeFormatsOffersAllThree()
    {
        IReadOnlyList<VideoCodec> offered = await Offering(
            LiveEncoder.Vaapi,
            MachineCapabilities.Of(
                CardStanding.Usable,
                [Faculty.EncodeH264OnTheCard, Faculty.EncodeH265OnTheCard, Faculty.EncodeAv1OnTheCard],
                string.Empty));

        Assert.Equal([VideoCodec.H264, VideoCodec.H265, VideoCodec.Av1], offered);
    }

    [Theory(DisplayName = "a format the card did not encode a frame of is not offered")]
    [InlineData(Faculty.EncodeH265OnTheCard, VideoCodec.H265)]
    [InlineData(Faculty.EncodeAv1OnTheCard, VideoCodec.Av1)]
    public async Task AFormatTheCardDidNotEncodeAFrameOfIsNotOffered(Faculty alone, VideoCodec format)
    {
        IReadOnlyList<VideoCodec> offered = await Offering(
            LiveEncoder.Vaapi,
            MachineCapabilities.Of(CardStanding.Usable, [Faculty.EncodeH264OnTheCard, alone], string.Empty));

        Assert.Equal([VideoCodec.H264, format], offered);
    }

    [Theory(DisplayName = "a card this machine cannot encode on offers H.264 alone, which the processor encodes")]
    [InlineData(CardStanding.NodeMissing)]
    [InlineData(CardStanding.DriverUnusable)]
    [InlineData(CardStanding.ProbeTimedOut)]
    public async Task ACardThisMachineCannotEncodeOnOffersH264Alone(CardStanding standing)
    {
        IReadOnlyList<VideoCodec> offered = await Offering(LiveEncoder.Vaapi, MachineCapabilities.Of(standing, [], "turned down"));

        Assert.Equal([VideoCodec.H264], offered);
    }

    [Fact(DisplayName = "live set to the processor offers H.264 alone, without asking about the card")]
    public async Task LiveSetToTheProcessorOffersH264AloneWithoutAskingAboutTheCard()
    {
        WhatIsAsked machine = new(MachineCapabilities.Of(
            CardStanding.Usable,
            [Faculty.EncodeH264OnTheCard, Faculty.EncodeH265OnTheCard, Faculty.EncodeAv1OnTheCard],
            string.Empty));

        IReadOnlyList<VideoCodec> offered = await new LiveEncoderSelection(new LiveTranscodeSettings(), machine)
            .FormatsAsync(CancellationToken.None);

        Assert.Equal([VideoCodec.H264], offered);
        Assert.Equal(0, machine.Times);
    }

    [Fact]
    public void ACardThisMachineCanEncodeOnIsNotAReasonToFallBack()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => LiveEncoderChoice.FellBackToSoftware(CardStanding.Usable, "no reason at all"));

    private static MachineCapabilities WithACard
        => MachineCapabilities.Of(CardStanding.Usable, [Faculty.EncodeH264OnTheCard], string.Empty);

    private static Task<IReadOnlyList<VideoCodec>> Offering(LiveEncoder prefer, MachineCapabilities can)
        => new LiveEncoderSelection(new LiveTranscodeSettings { Prefer = prefer }, new WhatIsAsked(can))
            .FormatsAsync(CancellationToken.None);

    private static Task<LiveEncoderChoice> Choosing(LiveEncoder prefer, MachineCapabilities can)
        => new LiveEncoderSelection(new LiveTranscodeSettings { Prefer = prefer }, new WhatIsAsked(can))
            .ChooseAsync(CancellationToken.None);

    private sealed class WhatIsAsked(MachineCapabilities can) : IMachineCapabilityReader
    {
        public int Times { get; private set; }

        public Task<MachineCapabilities> ReadAsync(CancellationToken cancellationToken)
        {
            Times++;

            return Task.FromResult(can);
        }
    }
}
