using System.Net;
using System.Text.RegularExpressions;

using Carina.Api.Live;
using Carina.Domain.Auth;
using Carina.Domain.Recordings;
using Carina.Domain.Streaming;
using Carina.Domain.Viewing;
using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

namespace Carina.Api.Tests.FeatureTest;

public sealed partial class RecordingHlsTests
{
    private const uint OneFrame = 1001;

    private static readonly IReadOnlyDictionary<uint, Fmp4TrackDefaults> Tracks = new Dictionary<uint, Fmp4TrackDefaults>
    {
        [Fmp4Media.PictureTrack] = new(30_000, 0, 0, 0),
        [Fmp4Media.SoundTrack] = new(48_000, 1024, 0, Fmp4Media.SyncSample),
    };

    [Fact(DisplayName = "FR-017: a recording's playlist, asked for with the session, names the playlist of one key and is transcoded in H.264 from the second asked for")]
    public async Task ARecordingsPlaylistNamesThePlaylistOfOneKeyAndIsTranscodedInH264FromTheSecondAskedFor()
    {
        await using PlayFeature feature = Transcoding();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        using HttpResponseMessage answer = await PlaylistAsync(feature.Client, recording, "?from=90.5&profile=720p30&format=av1");
        string playlist = await answer.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(LiveHlsPlaylists.MediaType, answer.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store, private", answer.Headers.CacheControl?.ToString());
        Assert.Single(Regex.Matches(playlist, "#EXT-X-STREAM-INF:"));
        Assert.Contains(",RESOLUTION=1280x720,FRAME-RATE=29.970,CODECS=\"avc1.640028,mp4a.40.2\"\n", playlist, StringComparison.Ordinal);
        Assert.True(Unguessable.IsOne(KeyIn(playlist)));
        Assert.Equal([TimeSpan.FromSeconds(90.5)], feature.Player.AskedFrom);
        Assert.Equal(["720p30"], feature.Player.AskedFor);
        Assert.Equal([VideoCodec.H264], feature.Player.AskedIn);
    }

    [Fact(DisplayName = "BR-PD-022: the playlist of a recording's key is an event from its first segment that ends once the recording is transcoded to its end")]
    public async Task ThePlaylistOfARecordingsKeyIsAnEventThatEndsOnceTheRecordingIsTranscodedToItsEnd()
    {
        await using PlayFeature feature = Transcoding();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        string key = KeyIn(await (await PlaylistAsync(feature.Client, recording)).Content.ReadAsStringAsync());
        HttpClient player = feature.Client;

        await Eventually.Happens(() => feature.Player.Handed?.WasLetGo is true, "the transcoder was let go of once it ended");

        using HttpResponseMessage listed = await player.GetAsync(new Uri($"/api/hls/{key}/index.m3u8", UriKind.Relative));
        using HttpResponseMessage initialisation = await player.GetAsync(new Uri($"/api/hls/{key}/init.mp4", UriKind.Relative));
        using HttpResponseMessage first = await player.GetAsync(new Uri($"/api/hls/{key}/0.m4s", UriKind.Relative));
        using HttpResponseMessage last = await player.GetAsync(new Uri($"/api/hls/{key}/3.m4s", UriKind.Relative));
        string playlist = await listed.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        Assert.Contains("#EXT-X-MEDIA-SEQUENCE:0\n#EXT-X-PLAYLIST-TYPE:EVENT\n", playlist, StringComparison.Ordinal);
        Assert.Equal(4, Regex.Matches(playlist, "#EXTINF:").Count);
        Assert.EndsWith("3.m4s\n#EXT-X-ENDLIST\n", playlist, StringComparison.Ordinal);
        Assert.Equal(Fmp4Media.Initialisation(), await initialisation.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.True(Fmp4Fragment.Read(await first.Content.ReadAsByteArrayAsync(), Tracks)!.Of(Fmp4Media.PictureTrack)!.Samples[0].IsASync);
        Assert.Equal(HttpStatusCode.OK, last.StatusCode);
    }

    [Fact(DisplayName = "BR-PD-022: a recording's playlist asked for from no second starts where this reader left the recording, as playing it does")]
    public async Task ARecordingsPlaylistAskedForFromNoSecondStartsWhereThisReaderLeftIt()
    {
        await using PlayFeature feature = Transcoding();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Positions.Positions.Add(PlaybackPosition.Reached(
            recording.Id,
            new Subject(TestAuthenticationHandler.Tester),
            TimeSpan.FromSeconds(300),
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)));

        using HttpResponseMessage answer = await PlaylistAsync(feature.Client, recording);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal([TimeSpan.FromSeconds(300)], feature.Player.AskedFrom);
    }

    [Fact(DisplayName = "BR-PV-005: a recording's playlist is opened with the reader's own session and nothing else")]
    public async Task ARecordingsPlaylistIsOpenedWithTheReadersOwnSession()
    {
        await using PlayFeature feature = Transcoding();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        using HttpResponseMessage answer = await PlaylistAsync(feature.Stranger, recording);

        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
        Assert.Empty(feature.Player.AskedFrom);
    }

    [Theory]
    [InlineData("?from=-1")]
    [InlineData("?from=soon")]
    [InlineData("?profile=4k")]
    [InlineData("?sound=fourth")]
    public async Task WhatIsNotOneOfTheThingsAPlaylistIsAskedByIsRefusedBeforeAnythingIsTranscoded(string query)
    {
        await using PlayFeature feature = Transcoding();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        using HttpResponseMessage answer = await PlaylistAsync(feature.Client, recording, query);

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
        Assert.Empty(feature.Player.AskedFrom);
    }

    [Fact]
    public async Task AnIdThatIsNotOneIsRefused()
    {
        await using PlayFeature feature = Transcoding();

        using HttpResponseMessage answer = await feature.Client.GetAsync(new Uri("/api/videos/not-an-id/hls.m3u8", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
    }

    [Fact]
    public async Task ARecordingWhoseFileIsGoneOrThatIsStillBeingWrittenIsRefusedAsPlayingItIs()
    {
        await using PlayFeature feature = Transcoding();

        using HttpResponseMessage gone = await PlaylistAsync(feature.Client, feature.Ended(RecordingOutcome.Complete, onDisk: false));
        using HttpResponseMessage writing = await PlaylistAsync(feature.Client, feature.StillWriting());

        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, writing.StatusCode);
        Assert.Empty(feature.Player.AskedFrom);
    }

    [Fact(DisplayName = "BR-PD-022: a recording's playlist takes a transcoder under the same limit as playing it, and is refused when as many are running as may be")]
    public async Task ARecordingsPlaylistIsRefusedWhenAsManyTranscodersAreRunningAsMayBe()
    {
        await using PlayFeature feature = Transcoding();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Player.Refuses = OnTheFlyRefusal.TooManyAlready;

        using HttpResponseMessage answer = await PlaylistAsync(feature.Client, recording);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, answer.StatusCode);
        Assert.Equal(LiveHlsDelivery.RefusalMediaType, answer.Content.Headers.ContentType?.ToString());
    }

    [Fact(DisplayName = "BR-PV-005: a recording whose transcoder writes no segment is refused, and the transcoder is let go of")]
    public async Task ARecordingWhoseTranscoderWritesNoSegmentIsRefused()
    {
        await using PlayFeature feature = Transcoding();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Player.Picture = [0, 0, 0, 16, .. "junk"u8.ToArray(), 1, 2, 3, 4, 5, 6, 7, 8];

        using HttpResponseMessage answer = await PlaylistAsync(feature.Client, recording);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, answer.StatusCode);
        await Eventually.Happens(() => feature.Player.Handed?.WasLetGo is true, "the transcoder was let go of");
    }

    private static PlayFeature Transcoding()
    {
        PlayFeature feature = new();

        feature.Player.Picture =
        [
            .. Fmp4Media.Initialisation(),
            .. Gop(0),
            .. Gop(1),
            .. Gop(2),
            .. Fmp4Media.Fragment(180 * OneFrame, [.. Enumerable.Repeat(OneFrame, 30)], true),
        ];

        return feature;
    }

    private static byte[] Gop(int number) => Fmp4Media.Fragment((ulong)number * 60 * OneFrame, [.. Enumerable.Repeat(OneFrame, 60)], true);

    private static Task<HttpResponseMessage> PlaylistAsync(HttpClient client, Recording recording, string query = "")
        => client.GetAsync(new Uri($"/api/videos/{recording.Id.Wire}/hls.m3u8{query}", UriKind.Relative));

    private static string KeyIn(string playlist) => KeyedPlaylist().Match(playlist).Groups[1].Value;

    [GeneratedRegex("/api/hls/([A-Za-z0-9_-]+)/index\\.m3u8")]
    private static partial Regex KeyedPlaylist();
}
