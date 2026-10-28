using System.Globalization;
using System.Text;

using Carina.Domain.Streaming;

namespace Carina.Api.Live;

/// <summary>
/// The two playlists a player reads a live channel or a recording through: the first, naming the key's own playlist
/// once, and the key's own, listing the segments.
/// </summary>
public static class LiveHlsPlaylists
{
    public const string MediaType = "application/vnd.apple.mpegurl";

    public const string Initialisation = "init.mp4";

    private const int Version = 7;

    public static string Multivariant(string key, LiveProfile profile, HlsWindow window)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(window);

        StringBuilder written = Head();

        written.Append("#EXT-X-INDEPENDENT-SEGMENTS\n");
        written.Append(CultureInfo.InvariantCulture, $"#EXT-X-STREAM-INF:BANDWIDTH={Bandwidth(window, profile)}");
        written.Append(CultureInfo.InvariantCulture, $",RESOLUTION={profile.Size.Width}x{profile.Size.Height}");
        written.Append(CultureInfo.InvariantCulture, $",FRAME-RATE={profile.Rate.PerSecond:0.000}");

        if (window.Codecs is { } codecs)
        {
            written.Append(CultureInfo.InvariantCulture, $",CODECS=\"{codecs}\"");
        }

        written.Append('\n');
        written.Append(CultureInfo.InvariantCulture, $"/api/hls/{key}/index.m3u8\n");

        return written.ToString();
    }

    public static string Media(HlsWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        StringBuilder written = Head();

        written.Append(CultureInfo.InvariantCulture, $"#EXT-X-TARGETDURATION:{Math.Max(1, window.TargetSeconds)}\n");
        written.Append(CultureInfo.InvariantCulture, $"#EXT-X-MEDIA-SEQUENCE:{window.FirstNumber}\n");

        if (window.Type is HlsPlaylistType.Event)
        {
            written.Append("#EXT-X-PLAYLIST-TYPE:EVENT\n");
        }

        written.Append("#EXT-X-INDEPENDENT-SEGMENTS\n");
        written.Append(CultureInfo.InvariantCulture, $"#EXT-X-MAP:URI=\"{Initialisation}\"\n");

        foreach (HlsSegment segment in window.Segments)
        {
            written.Append(CultureInfo.InvariantCulture, $"#EXTINF:{segment.Length.TotalSeconds:0.000},\n");
            written.Append(CultureInfo.InvariantCulture, $"{segment.Number}.m4s\n");
        }

        if (window.Ended)
        {
            written.Append("#EXT-X-ENDLIST\n");
        }

        return written.ToString();
    }

    private static StringBuilder Head()
        => new StringBuilder("#EXTM3U\n").Append(CultureInfo.InvariantCulture, $"#EXT-X-VERSION:{Version}\n");

    /// <summary>
    /// The bits a second of the segment carrying the most, or what the profile is capped at when no segment is held.
    /// </summary>
    private static long Bandwidth(HlsWindow window, LiveProfile profile)
        => window.Segments
            .Select(segment => (long?)Math.Ceiling((decimal)segment.Bytes * 8 * TimeSpan.TicksPerSecond / segment.Length.Ticks))
            .Max() ?? (profile.SoftwareRateControl.KilobitsPerSecond * 1000L);
}
