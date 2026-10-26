using Carina.Domain.Streaming;

namespace Carina.Api.Playback;

public enum FormatAnswer
{
    Unasked = 1,

    Named = 2,

    NotOneOfThese = 3,
}

/// <summary>
/// The format a picture transcoded as it plays is asked for in, read from the query it was asked with.
/// </summary>
public sealed record AskedFormat
{
    private AskedFormat(FormatAnswer answer, VideoCodec format)
    {
        Answer = answer;
        Format = format;
    }

    public FormatAnswer Answer { get; }

    public VideoCodec Format { get; }

    public static AskedFormat Read(string? asked)
    {
        if (string.IsNullOrWhiteSpace(asked))
        {
            return new AskedFormat(FormatAnswer.Unasked, VideoCodecs.Unasked);
        }

        return VideoCodecs.Find(asked) is { } found
            ? new AskedFormat(FormatAnswer.Named, found)
            : new AskedFormat(FormatAnswer.NotOneOfThese, VideoCodecs.Unasked);
    }

    /// <summary>
    /// Why a picture asked for in <paramref name="format"/> is refused rather than transcoded in another.
    /// </summary>
    public static string NotTranscodedHere(VideoCodec format)
        => $"This machine does not transcode a picture in {VideoCodecs.NameOf(format)}, and a picture is not "
           + "handed over in another format than the one asked for. GET /api/live/profiles names the formats it "
           + "transcodes in.";
}
