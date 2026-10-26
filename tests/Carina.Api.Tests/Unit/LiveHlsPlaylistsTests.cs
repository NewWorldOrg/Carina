using Carina.Api.Live;
using Carina.Domain.Auth;
using Carina.Domain.Streaming;

namespace Carina.Api.Tests.Unit;

public sealed class LiveHlsPlaylistsTests
{
    private static readonly string Key = Unguessable.Issue();

    private static readonly TimeSpan TwoSeconds = TimeSpan.FromTicks(2 * 1001 * TimeSpan.TicksPerSecond / 1000);

    [Fact(DisplayName = "BR-PV-005: the first answer is a playlist naming the key's own playlist once, with what a player chooses it by")]
    public void TheFirstAnswerNamesTheKeysOwnPlaylistOnce()
    {
        LiveHlsWindow window = new([new LiveHlsSegment(0, TwoSeconds, 500_500)], 2, false, "avc1.64002a,mp4a.40.2");

        Assert.Equal(
            "#EXTM3U\n"
            + "#EXT-X-VERSION:7\n"
            + "#EXT-X-INDEPENDENT-SEGMENTS\n"
            + "#EXT-X-STREAM-INF:BANDWIDTH=2000000,RESOLUTION=1920x1080,FRAME-RATE=59.940,CODECS=\"avc1.64002a,mp4a.40.2\"\n"
            + $"/api/hls/{Key}/index.m3u8\n",
            LiveHlsPlaylists.Multivariant(Key, LiveProfile.FullHd60, window));
    }

    [Fact]
    public void CodecsTheInitialisationDoesNotNameAreLeftUnsaid()
    {
        LiveHlsWindow window = new([new LiveHlsSegment(0, TwoSeconds, 1_000)], 2, false, null);

        Assert.DoesNotContain("CODECS", LiveHlsPlaylists.Multivariant(Key, LiveProfile.Hd30, window), StringComparison.Ordinal);
        Assert.Contains("RESOLUTION=1280x720,FRAME-RATE=29.970", LiveHlsPlaylists.Multivariant(Key, LiveProfile.Hd30, window), StringComparison.Ordinal);
    }

    [Fact]
    public void TheBandwidthIsThatOfTheSegmentCarryingTheMostAndNoLess()
    {
        LiveHlsWindow window = new(
            [new LiveHlsSegment(3, TwoSeconds, 1_000_000), new LiveHlsSegment(4, TwoSeconds, 1_500_001)],
            2,
            false,
            null);

        Assert.Contains("BANDWIDTH=5994010,", LiveHlsPlaylists.Multivariant(Key, LiveProfile.Hd30, window), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-PD-022: the playlist of a key maps the initialisation and lists the segments held, numbered from the oldest")]
    public void ThePlaylistOfAKeyMapsTheInitialisationAndListsTheSegmentsHeld()
    {
        LiveHlsWindow window = new(
            [new LiveHlsSegment(12, TwoSeconds, 10), new LiveHlsSegment(13, TimeSpan.FromSeconds(1.5), 10)],
            2,
            false,
            null);

        Assert.Equal(
            "#EXTM3U\n"
            + "#EXT-X-VERSION:7\n"
            + "#EXT-X-TARGETDURATION:2\n"
            + "#EXT-X-MEDIA-SEQUENCE:12\n"
            + "#EXT-X-INDEPENDENT-SEGMENTS\n"
            + "#EXT-X-MAP:URI=\"init.mp4\"\n"
            + "#EXTINF:2.002,\n"
            + "12.m4s\n"
            + "#EXTINF:1.500,\n"
            + "13.m4s\n",
            LiveHlsPlaylists.Media(window));
    }

    [Fact]
    public void ThePlaylistOfASessionThatHasEndedSaysNoMoreIsComing()
    {
        LiveHlsWindow window = new([new LiveHlsSegment(0, TwoSeconds, 10)], 2, true, null);

        Assert.EndsWith("0.m4s\n#EXT-X-ENDLIST\n", LiveHlsPlaylists.Media(window), StringComparison.Ordinal);
    }

    [Fact]
    public void ATargetNotYetLearntIsOneSecond()
    {
        Assert.Contains("#EXT-X-TARGETDURATION:1\n", LiveHlsPlaylists.Media(LiveHlsWindow.Empty), StringComparison.Ordinal);
    }
}
