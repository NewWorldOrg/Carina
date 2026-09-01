using Carina.Domain.Recordings;

namespace Carina.Api.Playback;

public static class PlaybackMediaType
{
    public const string TransportStream = "video/mp2t";

    public const string Mp4 = "video/mp4";

    public const string Unknown = "application/octet-stream";

    public static string Of(RecordingFileName fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        return Path.GetExtension(fileName.Value).ToLowerInvariant() switch
        {
            ".ts" or ".m2ts" or ".mts" => TransportStream,
            ".mp4" or ".m4v" => Mp4,
            _ => Unknown,
        };
    }
}
