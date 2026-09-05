using System.Runtime.Versioning;

using Carina.Domain.Machines;
using Carina.Infrastructure.Machines;

namespace Carina.Infrastructure.Tests.Machines;

/// <summary>
/// Read against the ffmpeg the application itself runs, rather than against a stand-in, so that
/// what the reader believes about this build stays true when the build changes.
/// </summary>
[SupportedOSPlatform("linux")]
[Trait("Category", "Material")]
public sealed class ThisMachineTests
{
    private static Task<MachineCapabilities> Reading()
        => new MachineCapabilityReader(new MachineSettings(), TimeProvider.System).ReadAsync(CancellationToken.None);

    [Fact(DisplayName = "BR-EV-004: the build the application runs encodes H.264 on the processor")]
    public async Task TheBuildTheApplicationRunsEncodesH264OnTheProcessor()
        => Assert.True((await Reading()).Has(Faculty.EncodeH264OnTheProcessor));

    [Fact(DisplayName = "BR-EV-004: the build the application runs has no libx265, so H.265 is the card's alone")]
    public async Task TheBuildTheApplicationRunsHasNoSoftwareH265()
        => Assert.False((await Reading()).Has(Faculty.EncodeH265OnTheProcessor));

    [Fact(DisplayName = "A-エンコード-000e: the build the application runs decodes ARIB captions")]
    public async Task TheBuildTheApplicationRunsDecodesAribCaptions()
        => Assert.True((await Reading()).Has(Faculty.DecodeAribCaptions));

    [Fact(DisplayName = "BR-EV-004: a machine with no card handed to it reads as one, rather than failing")]
    public async Task AMachineWithNoCardHandedToItReadsAsOneRatherThanFailing()
    {
        MachineCapabilities can = await Reading();

        Assert.Contains(can.Card, Enum.GetValues<CardStanding>());
        Assert.Equal(can.CardIsUsable, can.Has(Faculty.EncodeH264OnTheCard));
        if (can.CardIsUsable)
        {
            Assert.Equal(string.Empty, can.Note);
        }
        else
        {
            Assert.NotEmpty(can.Note);
        }
    }

    [Fact(DisplayName = "BR-EV-004: H.265 on the card is claimed exactly when a frame is actually encoded with hevc_vaapi on this machine")]
    public async Task H265OnTheCardIsClaimedExactlyWhenAFrameIsEncodedWithIt()
    {
        MachineCapabilities can = await Reading();
        ProgrammeSaid tried = await AnotherProgramme.SayAsync(
            new MachineSettings().Programme,
            VaapiProbeInvocation.Arguments(MachineSettings.TheRenderNode, FfmpegFaculties.H265OnTheCard),
            TimeSpan.FromSeconds(30),
            TimeProvider.System,
            CancellationToken.None);

        Assert.Equal(tried.Ran && tried.ExitCode is 0, can.Has(Faculty.EncodeH265OnTheCard));
    }

    [Fact]
    public async Task WhatThisMachineSaysNamesNoPathOnIt()
        => Assert.DoesNotContain('/', (await Reading()).Note);
}
