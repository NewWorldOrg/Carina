using System.Text.Json;

using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Api.Tests.FeatureTest;

public sealed class ArtefactCodecInThePlanTests
{
    [Theory(DisplayName = "BR-PD-009: the plan names the codec the artefact's file was read as, whatever its profile says now")]
    [InlineData(EncodeCodec.H264, EncodeCodec.H264, "h264")]
    [InlineData(EncodeCodec.H265, EncodeCodec.H264, "h264")]
    [InlineData(EncodeCodec.H265, EncodeCodec.H265, "h265")]
    [InlineData(EncodeCodec.H264, EncodeCodec.H265, "h265")]
    public async Task ThePlanNamesTheCodecTheArtefactsFileWasReadAs(EncodeCodec profile, EncodeCodec file, string named)
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, profile, fileReadAs: file, taggedAs: "hvc1");

        JsonElement read = await PlanAsync(feature, recording);

        Assert.Equal(named, read.GetProperty("artefactCodec").GetString());
    }

    [Fact(DisplayName = "BR-PD-009: an artefact in H.265 a browser cannot play is still named, while the recording is transcoded")]
    public async Task AnArtefactInH265ABrowserCannotPlayIsStillNamed()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, EncodeCodec.H265, fileReadAs: EncodeCodec.H265, taggedAs: "hvc1");

        JsonElement read = await PlanAsync(feature, recording);

        Assert.True(read.GetProperty("transcodes").GetBoolean());
        Assert.Equal("h265", read.GetProperty("artefactCodec").GetString());
    }

    [Fact(DisplayName = "BR-PD-009: the artefact is named when the recording itself is asked for")]
    public async Task TheArtefactIsNamedWhenTheRecordingItselfIsAskedFor()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, EncodeCodec.H264, fileReadAs: EncodeCodec.H264);

        JsonElement read = await PlanAsync(feature, recording, "?source=recording");

        Assert.Equal("recording", read.GetProperty("source").GetString());
        Assert.Equal("h264", read.GetProperty("artefactCodec").GetString());
    }

    [Fact(DisplayName = "BR-PD-009: an artefact whose file could not be read is named by no codec rather than by its profile")]
    public async Task AnArtefactWhoseFileCouldNotBeReadIsNamedByNoCodec()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, EncodeCodec.H264);

        JsonElement read = await PlanAsync(feature, recording);

        Assert.Equal(JsonValueKind.Null, read.GetProperty("artefactCodec").ValueKind);
    }

    [Fact(DisplayName = "BR-PD-009: a recording with no artefact is named by no codec")]
    public async Task ARecordingWithNoArtefactIsNamedByNoCodec()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        JsonElement read = await PlanAsync(feature, recording);

        Assert.Equal(JsonValueKind.Null, read.GetProperty("artefactCodec").ValueKind);
    }

    private static async Task<JsonElement> PlanAsync(PlayFeature feature, Recording recording, string query = "")
    {
        using HttpResponseMessage plan = await feature.PlanAsync(recording, query);

        return (await PlayFeature.PlanOfAsync(plan)).GetProperty("data");
    }
}
