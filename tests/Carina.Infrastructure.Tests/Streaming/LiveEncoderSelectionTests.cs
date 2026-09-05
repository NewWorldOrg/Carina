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

    [Fact]
    public void ACardThisMachineCanEncodeOnIsNotAReasonToFallBack()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => LiveEncoderChoice.FellBackToSoftware(CardStanding.Usable, "no reason at all"));

    private static MachineCapabilities WithACard
        => MachineCapabilities.Of(CardStanding.Usable, [Faculty.EncodeH264OnTheCard], string.Empty);

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
