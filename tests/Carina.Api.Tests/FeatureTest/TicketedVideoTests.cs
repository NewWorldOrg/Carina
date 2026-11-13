using System.Net;
using System.Text.Json;

using Carina.Domain.Encodings;
using Carina.Domain.Playback;
using Carina.Domain.Recordings;

namespace Carina.Api.Tests.FeatureTest;

public sealed class TicketedVideoTests
{
    [Fact]
    public async Task ATicketIsIssuedForOneRecordingAndSaysWhenItDies()
    {
        await using var feature = new TicketedFeature();
        Recording recording = feature.Ended();

        using HttpResponseMessage answer = await feature.AskForATicketAsync(recording.Id);
        using JsonDocument read = JsonDocument.Parse(await answer.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(43, read.RootElement.GetProperty("data").GetProperty("inTheClear").GetString()!.Length);
        Assert.True(read.RootElement.GetProperty("data").TryGetProperty("lapsesAt", out _));
    }

    [Fact(DisplayName = "a player with a ticket asking for the artefact is handed the file made in H.264 after its profile was changed to H.265")]
    public async Task APlayerAskingForTheArtefactIsHandedTheFileMadeInH264AfterItsProfileWasChangedToH265()
    {
        await using var feature = new TicketedFeature();
        Recording recording = feature.Ended();
        byte[] artefact = feature.Encoded(recording, EncodeCodec.H265, EncodeCodec.H264);
        string ticket = await feature.TicketForAsync(recording.Id);

        using HttpResponseMessage answer = await feature.AsAPlayerAsync(
            $"/api/videos/{recording.Id.Wire}?source=artefact",
            ticket);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal("video/mp4", answer.Content.Headers.ContentType?.MediaType);
        Assert.Equal(artefact, await answer.Content.ReadAsByteArrayAsync());
    }

    [Theory(DisplayName = "a player with a ticket is handed an artefact made in H.265, however it is tagged, which no browser's decoding narrows")]
    [InlineData("", "hvc1")]
    [InlineData("?source=artefact", "hev1")]
    public async Task APlayerWithATicketIsHandedAnArtefactMadeInH265(string query, string tag)
    {
        await using var feature = new TicketedFeature();
        Recording recording = feature.Ended();
        byte[] artefact = feature.Encoded(recording, EncodeCodec.H265, EncodeCodec.H265, tag);
        string ticket = await feature.TicketForAsync(recording.Id);

        using HttpResponseMessage answer = await feature.AsAPlayerAsync(
            $"/api/videos/{recording.Id.Wire}{query}",
            ticket);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal("video/mp4", answer.Content.Headers.ContentType?.MediaType);
        Assert.Equal(artefact, await answer.Content.ReadAsByteArrayAsync());
    }

    [Fact(DisplayName = "a player with a ticket is not handed an artefact whose file is neither of the two codecs, and gets the recording itself")]
    public async Task APlayerWithATicketIsNotHandedAnArtefactOfNeitherCodec()
    {
        await using var feature = new TicketedFeature();
        Recording recording = feature.Ended();
        feature.Encoded(recording, EncodeCodec.H265, EncodeCodec.H265);
        feature.Codecs.Readings[feature.Jobs.Jobs[0].ArtefactName!.Value] = ArtefactCodecReading.Neither("av1");
        string ticket = await feature.TicketForAsync(recording.Id);

        using HttpResponseMessage answer = await feature.AsAPlayerAsync($"/api/videos/{recording.Id.Wire}", ticket);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(feature.Written, await answer.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task APlayerThatAsksTheHeadersThenSeeksTwiceIsAdmittedEveryTime()
    {
        await using var feature = new TicketedFeature();
        Recording recording = feature.Ended();
        string ticket = await feature.TicketForAsync(recording.Id);
        string path = $"/api/videos/{recording.Id.Wire}";

        using HttpResponseMessage headers = await feature.AsAPlayerAsync(path, ticket, method: HttpMethod.Head);
        using HttpResponseMessage whole = await feature.AsAPlayerAsync(path, ticket, "bytes=0-");
        using HttpResponseMessage middle = await feature.AsAPlayerAsync(path, ticket, "bytes=1000-1999");
        using HttpResponseMessage tail = await feature.AsAPlayerAsync(path, ticket, "bytes=-500");

        Assert.Equal(HttpStatusCode.OK, headers.StatusCode);
        Assert.Equal(feature.Written.Length, headers.Content.Headers.ContentLength);
        Assert.Equal(HttpStatusCode.PartialContent, whole.StatusCode);
        Assert.Equal(feature.Written, await whole.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.PartialContent, middle.StatusCode);
        Assert.Equal(feature.Written[1000..2000], await middle.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.PartialContent, tail.StatusCode);
        Assert.Equal(feature.Written[^500..], await tail.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AGrantOpensTheOneRecordingItWasEnteredWithAndNoOther()
    {
        await using var feature = new TicketedFeature();
        Recording mine = feature.Ended();
        Recording another = feature.Ended();
        string ticket = await feature.TicketForAsync(mine.Id);

        using HttpResponseMessage entered = await feature.AsAPlayerAsync($"/api/videos/{mine.Id.Wire}", ticket);
        using HttpResponseMessage elsewhere = await feature.AsAPlayerAsync(
            $"/api/videos/{another.Id.Wire}",
            ticket);

        Assert.Equal(HttpStatusCode.OK, entered.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, elsewhere.StatusCode);
    }

    [Fact]
    public async Task BrLa004ATicketIssuedForOneRecordingOpensNoOtherEvenOnItsFirstUse()
    {
        await using var feature = new TicketedFeature();
        Recording mine = feature.Ended();
        Recording another = feature.Ended();
        string ticket = await feature.TicketForAsync(mine.Id);

        using HttpResponseMessage elsewhere = await feature.AsAPlayerAsync(
            $"/api/videos/{another.Id.Wire}",
            ticket,
            "bytes=0-");

        Assert.Equal(HttpStatusCode.Forbidden, elsewhere.StatusCode);
        Assert.NotEqual(feature.Written, await elsewhere.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task BrLa004ATicketOfferedAtTheWrongRecordingIsSpentAndOpensItsOwnNoLonger()
    {
        await using var feature = new TicketedFeature();
        Recording mine = feature.Ended();
        Recording another = feature.Ended();
        string ticket = await feature.TicketForAsync(mine.Id);

        using HttpResponseMessage elsewhere = await feature.AsAPlayerAsync($"/api/videos/{another.Id.Wire}", ticket);
        using HttpResponseMessage afterwards = await feature.AsAPlayerAsync($"/api/videos/{mine.Id.Wire}", ticket);

        Assert.Equal(HttpStatusCode.Forbidden, elsewhere.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, afterwards.StatusCode);
    }

    [Fact]
    public async Task APlayerCarryingNothingIsRefusedWithoutBeingSentToASignInScreen()
    {
        await using var feature = new TicketedFeature();
        Recording recording = feature.Ended();

        using HttpResponseMessage answer = await feature.AsAPlayerAsync($"/api/videos/{recording.Id.Wire}", null);

        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
        Assert.Null(answer.Headers.Location);
        Assert.Empty(await answer.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task SomethingThatWasNeverIssuedIsRefusedBeforeAnyByteLeaves()
    {
        await using var feature = new TicketedFeature();
        Recording recording = feature.Ended();

        using HttpResponseMessage answer = await feature.AsAPlayerAsync(
            $"/api/videos/{recording.Id.Wire}",
            new string('a', 43));

        byte[] body = await answer.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.Forbidden, answer.StatusCode);
        Assert.NotEqual(feature.Written, body);
    }

    [Theory]
    [InlineData("/play")]
    [InlineData("/thumbnail")]
    [InlineData("/scrub")]
    [InlineData("/captions")]
    [InlineData("/data-broadcast")]
    [InlineData("/data-broadcast/modules/64/7/0/1")]
    public async Task TheTicketOpensTheBytesAndNothingElseUnderTheSamePrefix(string beneath)
    {
        await using var feature = new TicketedFeature();
        Recording recording = feature.Ended();
        string ticket = await feature.TicketForAsync(recording.Id);

        using HttpResponseMessage answer = await feature.AsAPlayerAsync(
            $"/api/videos/{recording.Id.Wire}{beneath}",
            ticket);

        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
        Assert.Null(answer.Headers.Location);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/play")]
    [InlineData("/thumbnail")]
    [InlineData("/scrub")]
    [InlineData("/captions")]
    [InlineData("/data-broadcast")]
    [InlineData("/data-broadcast/modules/64/7/0/1")]
    public async Task EverySurfaceUnderThisPrefixRefusesAStrangerWithTheSameStatus(string beneath)
    {
        await using var feature = new TicketedFeature();
        Recording recording = feature.Ended();

        using HttpResponseMessage answer = await feature.AsAPlayerAsync(
            $"/api/videos/{recording.Id.Wire}{beneath}",
            null);

        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
        Assert.Null(answer.Headers.Location);
    }

    [Fact]
    public async Task TheAnswerToATicketIsNeverHeldByAnythingInFront()
    {
        await using var feature = new TicketedFeature();
        Recording recording = feature.Ended();
        string ticket = await feature.TicketForAsync(recording.Id);

        using HttpResponseMessage answer = await feature.AsAPlayerAsync($"/api/videos/{recording.Id.Wire}", ticket);

        Assert.Equal("no-store, private", answer.Headers.CacheControl?.ToString());
        Assert.Contains("Authorization", answer.Headers.Vary, StringComparer.OrdinalIgnoreCase);
    }
}
