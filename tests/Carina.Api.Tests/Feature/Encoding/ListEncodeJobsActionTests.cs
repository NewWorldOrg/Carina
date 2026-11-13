using System.Net;
using System.Text.Json;

using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;
using Carina.Domain.Streaming;

namespace Carina.Api.Tests.Feature.Encoding;

[Trait("Category", "Feature")]
public sealed class ListEncodeJobsActionTests
{
    [Fact(DisplayName = "the list says when a job's artefact was replaced by a newer one, and says nothing of it for the one that stands")]
    public async Task TheListSaysWhenAJobsArtefactWasReplaced()
    {
        await using EncodingFeature feature = new();
        EncodeProfile first = feature.Defined();
        EncodeProfile second = feature.Defined();
        EncodeDestination destination = feature.Placed(first);
        Recording recording = feature.Recorded();
        EncodeJob replaced = feature.Completed(recording, first, destination);
        EncodeJob standing = feature.Completed(recording, second, destination);
        DateTime replacedAt = EncodingFeature.Noon.AddMinutes(5);
        replaced.Replaced(standing, replacedAt);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"/api/encoding/jobs?recordingId={recording.Id.Wire}");
        JsonElement[] items = [.. body.GetProperty("data").GetProperty("items").EnumerateArray()];
        JsonElement readReplaced = items.Single(item => item.GetProperty("id").GetGuid() == replaced.Id.Value);
        JsonElement readStanding = items.Single(item => item.GetProperty("id").GetGuid() == standing.Id.Value);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(replacedAt, readReplaced.GetProperty("replacedAt").GetDateTime().ToUniversalTime());
        Assert.Equal(replaced.ArtefactName!.Value, readReplaced.GetProperty("artefactName").GetString());
        Assert.Equal(JsonValueKind.Null, readStanding.GetProperty("replacedAt").ValueKind);
    }

    [Fact(DisplayName = "the list says of each job where it stands, how it ran, how far it got, how long it has been quiet, whether that is a stall, and why it failed")]
    public async Task TheListSaysWhereEachJobStands()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        Recording recording = feature.Recorded();
        EncodeJob running = feature.Queued(recording, profile, destination);
        running.Start(EncodingFeature.Noon.AddMinutes(-25));
        running.Routed(new EncodeRoute(EncodeEncoder.Vaapi, EncodeEncoder.Software, EncodeSwerve.TheCardIsOutOfReach));
        running.Spawned(new RunningProgramme(4242, EncodingFeature.Noon.AddMinutes(-25)));
        running.Reached(EncodeProgress.Of(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), 2.0, false), EncodingFeature.Noon.AddMinutes(-15));
        running.Aligned(new EncodeTimeline(TimeSpan.FromSeconds(30499.474078), TimeSpan.FromSeconds(0.5072), TimeSpan.FromSeconds(2097.502489), null));
        running.Measured(TimeSpan.FromSeconds(2096.7947));
        EncodeJob failed = feature.Queued(feature.Recorded(), profile, destination);
        failed.Start(EncodingFeature.Noon.AddMinutes(-40));
        failed.Fail(EncodeFailure.NotEnoughRoom, "No space left on device", EncodingFeature.Noon.AddMinutes(-35));
        EncodeJob waiting = feature.Queued(feature.Recorded(), profile, destination);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/encoding/jobs");
        JsonElement data = body.GetProperty("data");
        JsonElement[] items = [.. data.GetProperty("items").EnumerateArray()];

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(3, data.GetProperty("total").GetInt32());
        Assert.Equal(EncodeJobQuery.DefaultPerPage, data.GetProperty("perPage").GetInt32());
        Assert.Equal(3, items.Length);
        JsonElement ran = items.Single(item => item.GetProperty("id").GetGuid() == running.Id.Value);
        Assert.Equal("running", ran.GetProperty("status").GetString());
        Assert.Equal("vaapi", ran.GetProperty("route").GetProperty("asked").GetString());
        Assert.Equal("software", ran.GetProperty("route").GetProperty("ran").GetString());
        Assert.Equal("theCardIsOutOfReach", ran.GetProperty("route").GetProperty("swerved").GetString());
        Assert.Equal(0.333, ran.GetProperty("headway").GetProperty("portion").GetDouble(), 3);
        Assert.Equal(600, ran.GetProperty("headway").GetProperty("leftSeconds").GetInt32());
        Assert.Equal(900, ran.GetProperty("quietForSeconds").GetInt32());
        Assert.True(ran.GetProperty("stalled").GetBoolean());
        Assert.DoesNotContain(Numbers(ran), number => number.GetRawText() == "4242");
        Assert.False(ran.TryGetProperty("programme", out _));
        JsonElement timeline = ran.GetProperty("timeline");
        Assert.Equal(30499.474078, timeline.GetProperty("sourceStartSeconds").GetDouble(), 6);
        Assert.Equal(0.5072, timeline.GetProperty("headSkipSeconds").GetDouble(), 6);
        Assert.Equal(30499.981278, timeline.GetProperty("captionShiftSeconds").GetDouble(), 6);
        Assert.Equal(2097.502489, timeline.GetProperty("sourceLengthSeconds").GetDouble(), 6);
        Assert.Equal(2096.7947, timeline.GetProperty("artefactLengthSeconds").GetDouble(), 6);
        Assert.Equal(-0.200589, timeline.GetProperty("driftSeconds").GetDouble(), 6);
        Assert.True(timeline.GetProperty("lengthsAgree").GetBoolean());
        JsonElement fell = items.Single(item => item.GetProperty("id").GetGuid() == failed.Id.Value);
        Assert.Equal("failed", fell.GetProperty("status").GetString());
        Assert.Equal("notEnoughRoom", fell.GetProperty("failure").GetProperty("failure").GetString());
        Assert.Equal("No space left on device", fell.GetProperty("failure").GetProperty("note").GetString());
        JsonElement waits = items.Single(item => item.GetProperty("id").GetGuid() == waiting.Id.Value);
        Assert.Equal("queued", waits.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, waits.GetProperty("quietForSeconds").ValueKind);
    }

    [Fact(DisplayName = "the list says why a job made its artefact again and what it kept, and says nothing of it for a job that made it once")]
    public async Task TheListSaysWhyAJobMadeItsArtefactAgain()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        EncodeJob remaking = feature.Queued(feature.Recorded(), profile, destination);
        remaking.Start(EncodingFeature.Noon.AddMinutes(-25));
        remaking.Remaking();
        EncodeJob larger = feature.Queued(feature.Recorded(), profile, destination);
        larger.Start(EncodingFeature.Noon.AddMinutes(-90));
        larger.Remaking();
        larger.MadeAgain(EncodeRemake.StillLargerThanTheSource);
        larger.Fail(EncodeFailure.NotEnoughRoom, "No space left on device", EncodingFeature.Noon.AddMinutes(-30));
        EncodeJob once = feature.Queued(feature.Recorded(), profile, destination);

        (_, JsonElement body) = await feature.GetAsync("/api/encoding/jobs");
        JsonElement[] items = [.. body.GetProperty("data").GetProperty("items").EnumerateArray()];

        Assert.Equal("largerThanTheSource", items.Single(item => item.GetProperty("id").GetGuid() == remaking.Id.Value).GetProperty("remade").GetString());
        Assert.Equal("stillLargerThanTheSource", items.Single(item => item.GetProperty("id").GetGuid() == larger.Id.Value).GetProperty("remade").GetString());
        Assert.Equal(JsonValueKind.Null, items.Single(item => item.GetProperty("id").GetGuid() == once.Id.Value).GetProperty("remade").ValueKind);
    }

    [Fact(DisplayName = "A job waiting because the card is making a picture for someone watching says so on the list, and a running one does not")]
    public async Task AJobWaitingBecauseSomeoneIsWatchingSaysSoOnTheList()
    {
        await using var feature = new EncodingFeature(EncodeEncoder.Vaapi);
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        EncodeJob running = feature.Queued(feature.Recorded(), profile, destination);
        running.Start(EncodingFeature.Noon.AddMinutes(-5));
        EncodeJob waiting = feature.Queued(feature.Recorded(), profile, destination);
        using ITranscodeSeat watched = feature.Transcoders.Claim(TranscodePurpose.Live).Seat!;

        (_, JsonElement body) = await feature.GetAsync("/api/encoding/jobs");
        JsonElement[] items = [.. body.GetProperty("data").GetProperty("items").EnumerateArray()];

        Assert.True(items.Single(item => item.GetProperty("id").GetGuid() == waiting.Id.Value)
            .GetProperty("waitingForAViewer").GetBoolean());
        Assert.False(items.Single(item => item.GetProperty("id").GetGuid() == running.Id.Value)
            .GetProperty("waitingForAViewer").GetBoolean());
    }

    [Fact(DisplayName = "A job waiting with nobody watching is not said to be waiting for a viewer")]
    public async Task AJobWaitingWithNobodyWatchingIsNotSaidToBeWaitingForAViewer()
    {
        await using var feature = new EncodingFeature(EncodeEncoder.Vaapi);
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        EncodeJob waiting = feature.Queued(feature.Recorded(), profile, destination);

        (_, JsonElement body) = await feature.GetAsync("/api/encoding/jobs");

        Assert.False(body.GetProperty("data").GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == waiting.Id.Value)
            .GetProperty("waitingForAViewer").GetBoolean());
    }

    [Fact(DisplayName = "A job the processor will encode does not wait for a viewer, so the list never says it does")]
    public async Task AJobTheProcessorWillEncodeDoesNotWaitForAViewer()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        EncodeJob waiting = feature.Queued(feature.Recorded(), profile, destination);
        using ITranscodeSeat watched = feature.Transcoders.Claim(TranscodePurpose.Live).Seat!;

        (_, JsonElement body) = await feature.GetAsync("/api/encoding/jobs");

        Assert.False(body.GetProperty("data").GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == waiting.Id.Value)
            .GetProperty("waitingForAViewer").GetBoolean());
    }

    [Fact]
    public async Task TheListIsNarrowedToTheStandingsAskedForAndPaged()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        feature.Queued(feature.Recorded(), profile, destination);
        feature.Queued(feature.Recorded(), profile, destination);
        EncodeJob ended = feature.Queued(feature.Recorded(), profile, destination);
        ended.Start(EncodingFeature.Noon.AddMinutes(-20));
        ended.Cancel(EncodingFeature.Noon.AddMinutes(-19));

        (_, JsonElement queued) = await feature.GetAsync("/api/encoding/jobs?status=queued&perPage=1");
        (_, JsonElement cancelled) = await feature.GetAsync("/api/encoding/jobs?status=cancelled&status=running");
        (HttpStatusCode refused, _) = await feature.GetAsync("/api/encoding/jobs?page=0");

        Assert.Equal(2, queued.GetProperty("data").GetProperty("total").GetInt32());
        Assert.Equal(1, queued.GetProperty("data").GetProperty("items").GetArrayLength());
        Assert.Equal(2, queued.GetProperty("data").GetProperty("lastPage").GetInt32());
        Assert.Equal(1, cancelled.GetProperty("data").GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, refused);
    }

    [Fact(DisplayName = "the ledger is asked about one recording, and answers with that recording's jobs only")]
    public async Task TheLedgerIsAskedAboutOneRecordingAndAnswersWithThatRecordingsJobsOnly()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        Recording asked = feature.Recorded();
        EncodeJob mine = feature.Queued(asked, profile, destination);
        feature.Queued(feature.Recorded(), profile, destination);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(
            $"/api/encoding/jobs?recordingId={asked.Id.Wire}");
        JsonElement item = Assert.Single(body.GetProperty("data").GetProperty("items").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, body.GetProperty("data").GetProperty("total").GetInt32());
        Assert.Equal(mine.Id.Value, item.GetProperty("id").GetGuid());
        Assert.Equal(asked.Id.Wire, item.GetProperty("recordingId").GetString());
    }

    [Fact(DisplayName = "a recording the ledger holds no job for is an empty page, not a refusal")]
    public async Task ARecordingTheLedgerHoldsNoJobForIsAnEmptyPage()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        feature.Queued(feature.Recorded(), profile, feature.Placed(profile));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(
            $"/api/encoding/jobs?recordingId={RecordingId.New().Wire}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(0, body.GetProperty("data").GetProperty("total").GetInt32());
        Assert.Empty(body.GetProperty("data").GetProperty("items").EnumerateArray());
    }

    [Theory]
    [InlineData("not-a-recording")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task AskingTheLedgerAboutSomethingThatIsNotARecordingIsRefused(string recordingId)
    {
        await using var feature = new EncodingFeature();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(
            $"/api/encoding/jobs?recordingId={recordingId}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("hexadecimal", body.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "naming a recording and a standing together narrows by both")]
    public async Task NamingARecordingAndAStandingTogetherNarrowsByBoth()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        Recording asked = feature.Recorded();
        feature.Queued(asked, profile, destination);

        (_, JsonElement waiting) = await feature.GetAsync(
            $"/api/encoding/jobs?recordingId={asked.Id.Wire}&status=queued");
        (_, JsonElement running) = await feature.GetAsync(
            $"/api/encoding/jobs?recordingId={asked.Id.Wire}&status=running");

        Assert.Equal(1, waiting.GetProperty("data").GetProperty("total").GetInt32());
        Assert.Equal(0, running.GetProperty("data").GetProperty("total").GetInt32());
    }


    private static IEnumerable<JsonElement> Numbers(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property => Numbers(property.Value)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(Numbers),
        JsonValueKind.Number => [element],
        _ => [],
    };
}
