using System.Net;
using System.Text.Json;

using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Api.Tests.Feature.Encoding;

[Trait("Category", "Feature")]
public sealed class GetEncodeSettingsActionTests
{
    [Fact(DisplayName = "a machine nobody has settled answers what it was deployed with, and says nobody settled it")]
    public async Task AMachineNobodyHasSettledAnswersWhatItWasDeployedWith()
    {
        await using var feature = new EncodingFeature();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/encoding/settings");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(data.GetProperty("automatically").GetBoolean());
        Assert.Equal(2, data.GetProperty("mostCores").GetInt32());
        Assert.Equal(6, data.GetProperty("coresThisMachineHas").GetInt32());
        Assert.False(data.GetProperty("stored").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("updatedAt").ValueKind);
    }

    [Fact(DisplayName = "what the auto-run takes is stated, and it is what ended with a file")]
    public async Task WhatTheAutoRunTakesIsStated()
    {
        await using var feature = new EncodingFeature();

        (_, JsonElement body) = await feature.GetAsync("/api/encoding/settings");

        Assert.Equal(
            ["complete", "truncated"],
            body.GetProperty("data").GetProperty("subject").EnumerateArray().Select(each => each.GetString()));
    }

    [Fact(DisplayName = "a machine offering more than one destination says the auto-run cannot settle where an artefact goes")]
    public async Task AMachineOfferingMoreThanOneDestinationSaysTheAutoRunCannotSettleWhereAnArtefactGoes()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        feature.Placed(profile);
        feature.Placed(profile, new OutputRoot("elsewhere"));

        (_, JsonElement body) = await feature.GetAsync("/api/encoding/settings");

        Assert.Equal("moreThanOneIsOffered", body.GetProperty("data").GetProperty("whereArtefactsGo").GetString());
    }

    [Fact(DisplayName = "one destination settles where an artefact goes, and a machine with none says nothing is defined")]
    public async Task OneDestinationSettlesWhereAnArtefactGoesAndNoneSaysNothingIsDefined()
    {
        await using var nothing = new EncodingFeature();
        await using var one = new EncodingFeature();
        one.Placed(one.Defined());

        (_, JsonElement undefined) = await nothing.GetAsync("/api/encoding/settings");
        (_, JsonElement settled) = await one.PutAsync("/api/encoding/settings", new { automatically = true, mostCores = 2 });

        Assert.Equal("nothingIsDefined", undefined.GetProperty("data").GetProperty("whereArtefactsGo").GetString());
        Assert.Equal("settled", settled.GetProperty("data").GetProperty("whereArtefactsGo").GetString());
    }
}
